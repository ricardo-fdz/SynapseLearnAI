using System.Text.Json;
using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.Conversations;
using LearningAgents.Application.Dtos.Messages;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.LLM;
using LearningAgents.Domain.Memory;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LearningAgents.Application.Services;

internal sealed class ConversationService(
    LearningAgentsDbContext dbContext,
    IPromptBuilder promptBuilder,
    ILLMProviderRouter llmProviderRouter,
    IMemoryToolHandler memoryToolHandler) : IConversationService
{
    private const int MaxToolIterations = 5;
    private const int MaxHistoryMessages = 30;

    public async Task<ServiceResult<ConversationMessageResponse>> SendMessageAsync(
        int sessionId,
        CreateConversationMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.StudySessions
            .AsNoTracking()
            .Include(studySession => studySession.Tutor)
            .FirstOrDefaultAsync(studySession => studySession.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<ConversationMessageResponse>.Failure("Not found");
        }

        var totalMessageCount = await dbContext.Messages
            .AsNoTracking()
            .Where(message => message.SessionId == sessionId)
            .CountAsync(cancellationToken);

        var recentMessages = await dbContext.Messages
            .AsNoTracking()
            .Where(message => message.SessionId == sessionId)
            .OrderByDescending(message => message.Id)
            .Take(MaxHistoryMessages)
            .Select(message => new LLMMessage(message.Role.ToString().ToLowerInvariant(), message.Content))
            .ToListAsync(cancellationToken);
        recentMessages.Reverse();
        var previousMessages = recentMessages;

        if (totalMessageCount > MaxHistoryMessages)
        {
            Console.WriteLine($"[H-013] Session {sessionId}: history truncated from {totalMessageCount} to {MaxHistoryMessages} messages for LLM context.");
        }

        var userMessage = new Message
        {
            SessionId = sessionId,
            Role = MessageRole.User,
            Content = request.Content,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Messages.Add(userMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        var workingMessages = previousMessages.ToList();
        workingMessages.Add(new LLMMessage("user", request.Content));

        var systemPrompt = await promptBuilder.BuildSystemPromptAsync(session.TutorId, request.Profile, cancellationToken);
        var estimatedInputChars = systemPrompt.Length + previousMessages.Sum(m => m.Content?.Length ?? 0) + (request.Content?.Length ?? 0);
        var estimatedTokens = estimatedInputChars / 4;
        Console.WriteLine($"[H-013] Prompt metrics session {sessionId}: systemPrompt={systemPrompt.Length} chars, history={previousMessages.Count}/{totalMessageCount} msgs, totalInput~{estimatedInputChars} chars (~{estimatedTokens} tokens), profile={request.Profile}");
        LLMResponse? llmResponse = null;
        var toolAttempts = new List<string>();
        var readMemoryKeys = new HashSet<string>(StringComparer.Ordinal);
        var lastSaveMemoryFailed = false;
        string? lastSaveMemoryFailure = null;
        for (var iteration = 1; iteration <= MaxToolIterations; iteration++)
        {
            var messagesForRequest = lastSaveMemoryFailed
                ? workingMessages.Append(new LLMMessage(
                    "system",
                    $"NOTA INTERNA: el intento de guardar memoria en este turno falló y no se corrigió. Tu respuesta final al usuario debe reflejar esto honestamente; no afirmes que el progreso o nivel se actualizó si la herramienta no tuvo éxito. Detalle del fallo: {lastSaveMemoryFailure}"))
                    .ToList()
                : workingMessages;

            llmResponse = await llmProviderRouter.GenerateAsync(
                session.Tutor.LlmProfile,
                new PromptRequest(session.Tutor.GeminiModel, systemPrompt, messagesForRequest, MemoryToolDeclarations.All),
                cancellationToken);

            if (!llmResponse.IsSuccess)
            {
                if (IsTemporaryGeminiFailure(llmResponse))
                {
                    Console.WriteLine($"Temporary Gemini failure for session {sessionId}: {llmResponse.ErrorMessage}");
                    var temporaryFailureMessage = new Message
                    {
                        SessionId = sessionId,
                        Role = MessageRole.Assistant,
                        Content = "Gemini está temporalmente sobrecargado. Tu mensaje fue recibido, intenta de nuevo en un momento.",
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    dbContext.Messages.Add(temporaryFailureMessage);
                    await dbContext.SaveChangesAsync(cancellationToken);

                    return ServiceResult<ConversationMessageResponse>.Success(new ConversationMessageResponse(ToResponse(temporaryFailureMessage)));
                }

                return ServiceResult<ConversationMessageResponse>.Failure(llmResponse.ErrorMessage ?? "Gemini request failed.");
            }

            var functionCalls = llmResponse.FunctionCalls ?? [];
            if (functionCalls.Count == 0)
            {
                break;
            }

            Console.WriteLine($"Tool calling iteration {iteration}: {functionCalls.Count} function call(s).");
            workingMessages.Add(LLMMessage.ForFunctionCalls(functionCalls));
            var toolResponses = new List<LLMFunctionResponse>();
            foreach (var functionCall in functionCalls)
            {
                var argsText = functionCall.Args.GetRawText();
                Console.WriteLine($"Executing tool '{functionCall.Name}' with args: {argsText}");
                var toolResponse = ShouldRejectSaveBeforeRead(functionCall, readMemoryKeys, out var guardError)
                    ? CreateToolErrorResponse(functionCall.Name, guardError)
                    : await memoryToolHandler.HandleAsync(
                        session.TutorId,
                        functionCall.Name,
                        functionCall.Args,
                        userMessage.Id,
                        cancellationToken);
                var responseForConversation = toolResponse with { Id = functionCall.Id };
                var responseText = responseForConversation.Response.GetRawText();
                Console.WriteLine($"Tool '{functionCall.Name}' response: {responseText}");
                toolAttempts.Add($"iteration={iteration}; tool={functionCall.Name}; args={argsText}; response={responseText}");
                toolResponses.Add(responseForConversation);

                TrackReadMemory(functionCall, responseForConversation, readMemoryKeys);
                if (functionCall.Name == "guardar_memoria")
                {
                    if (ToolSucceeded(responseForConversation.Response))
                    {
                        lastSaveMemoryFailed = false;
                        lastSaveMemoryFailure = null;
                    }
                    else
                    {
                        lastSaveMemoryFailed = true;
                        lastSaveMemoryFailure = responseText;
                    }
                }
            }

            workingMessages.Add(LLMMessage.ForFunctionResponses(toolResponses));
        }

        if (llmResponse is null || (llmResponse.FunctionCalls?.Count ?? 0) > 0)
        {
            Console.WriteLine(
                $"Tool calling iteration limit ({MaxToolIterations}) was reached without a final assistant response for session {sessionId}. Attempts: {string.Join(" | ", toolAttempts)}");

            var fallbackAssistantMessage = new Message
            {
                SessionId = sessionId,
                Role = MessageRole.Assistant,
                Content = "Hubo un problema técnico actualizando tu progreso, pero tu mensaje fue recibido. Intenta de nuevo en un momento.",
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.Messages.Add(fallbackAssistantMessage);
            await dbContext.SaveChangesAsync(cancellationToken);

            return ServiceResult<ConversationMessageResponse>.Success(new ConversationMessageResponse(ToResponse(fallbackAssistantMessage)));
        }

        var assistantMessage = new Message
        {
            SessionId = sessionId,
            Role = MessageRole.Assistant,
            Content = llmResponse.Content ?? string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Messages.Add(assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ConversationMessageResponse>.Success(new ConversationMessageResponse(ToResponse(assistantMessage)));
    }

    private static bool ShouldRejectSaveBeforeRead(
        LLMFunctionCall functionCall,
        HashSet<string> readMemoryKeys,
        out string error)
    {
        error = string.Empty;
        if (functionCall.Name != "guardar_memoria" || !TryGetPatchKeyAndOperation(functionCall.Args, out var key, out var operation))
        {
            return false;
        }

        if (operation is not ("Update" or "Resolve") || key is not (MemoryKeys.DomainMap or MemoryKeys.GapsOrErrors))
        {
            return false;
        }

        if (readMemoryKeys.Contains(key))
        {
            return false;
        }

        error = $"Debes llamar leer_memoria con key='{key}' antes de poder usar {operation} sobre esta memoria, para obtener el id real de los elementos. Luego usa ese campo 'id' como targetId; no uses el nombre, concepto o título visible. ACCIÓN REQUERIDA AHORA: llama leer_memoria con key='{key}' en tu siguiente respuesta, antes de generar cualquier mensaje de texto para el usuario. No respondas al usuario todavía; aún tienes la oportunidad de corregir esto en este mismo turno.";
        return true;
    }

    private static bool TryGetPatchKeyAndOperation(JsonElement args, out string key, out string operation)
    {
        key = string.Empty;
        operation = string.Empty;
        if (!args.TryGetProperty("patch", out var patch)
            || patch.ValueKind != JsonValueKind.Object
            || !patch.TryGetProperty("key", out var keyElement)
            || keyElement.ValueKind != JsonValueKind.String
            || !patch.TryGetProperty("operation", out var operationElement)
            || operationElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        key = keyElement.GetString() ?? string.Empty;
        operation = operationElement.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(operation);
    }

    private static LLMFunctionResponse CreateToolErrorResponse(string toolName, string error) =>
        new(toolName, JsonSerializer.SerializeToElement(new { success = false, error }));

    private static void TrackReadMemory(
        LLMFunctionCall functionCall,
        LLMFunctionResponse toolResponse,
        HashSet<string> readMemoryKeys)
    {
        if (functionCall.Name != "leer_memoria" || !ToolSucceeded(toolResponse.Response))
        {
            return;
        }

        if (toolResponse.Response.TryGetProperty("key", out var keyElement)
            && keyElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(keyElement.GetString()))
        {
            readMemoryKeys.Add(keyElement.GetString()!);
        }
    }

    private static bool ToolSucceeded(JsonElement response) =>
        response.TryGetProperty("success", out var successElement)
        && successElement.ValueKind is JsonValueKind.True;

    private static bool IsTemporaryGeminiFailure(LLMResponse response) =>
        response.StatusCode is 429 or 503;

    private static MessageResponse ToResponse(Message message) => new(
        message.Id,
        message.SessionId,
        message.Role.ToString().ToLowerInvariant(),
        message.Content,
        message.CreatedAtUtc);
}
