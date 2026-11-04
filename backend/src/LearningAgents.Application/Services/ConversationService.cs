using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.Conversations;
using LearningAgents.Application.Dtos.Messages;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.LLM;
using LearningAgents.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LearningAgents.Application.Services;

internal sealed class ConversationService(
    ILearningAgentsDbContext dbContext,
    IPromptBuilder promptBuilder,
    ILLMProviderRouter llmProviderRouter,
    IMemoryToolHandler memoryToolHandler,
    ILogger<ConversationService> logger) : IConversationService
{
    private const int MaxToolIterations = 5;
    private const int MaxHistoryMessages = 30;

    public async Task<ServiceResult<ConversationMessageResponse>> SendMessageAsync(
        int sessionId,
        CreateConversationMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var prep = await PrepareTurnContextAsync(sessionId, request, cancellationToken);
        if (!prep.IsSuccess)
        {
            return ServiceResult<ConversationMessageResponse>.Failure(prep.ErrorMessage!, prep.StatusCode);
        }

        var ctx = prep.Data!;
        var workingMessages = ctx.PreviousMessages.ToList();
        workingMessages.Add(new LLMMessage("user", ctx.UserMessage.Content));

        LLMResponse? llmResponse = null;
        var toolAttempts = new List<string>();
        var appliedMemoryChanges = new List<AppliedMemoryChangeSummary>();
        var readMemoryKeys = new HashSet<string>(StringComparer.Ordinal);
        var lastSaveMemoryFailed = false;
        string? lastSaveMemoryFailure = null;
        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;

        for (var iteration = 1; iteration <= MaxToolIterations; iteration++)
        {
            var messagesForRequest = BuildMessagesForIteration(workingMessages, lastSaveMemoryFailed, lastSaveMemoryFailure);

            llmResponse = await llmProviderRouter.GenerateAsync(
                ctx.Session.Tutor.LlmProfile,
                new PromptRequest(ctx.Session.Tutor.GeminiModel, ctx.SystemPrompt, messagesForRequest, MemoryToolDeclarations.All),
                cancellationToken);

            if (llmResponse.Usage is { } usage)
            {
                totalPromptTokens += usage.PromptTokens;
                totalCompletionTokens += usage.CompletionTokens;
                logger.LogInformation(
                    "LLM token usage session {SessionId} iteration {Iteration}: promptTokens={PromptTokens}, completionTokens={CompletionTokens}, totalTokens={TotalTokens}",
                    sessionId,
                    iteration,
                    usage.PromptTokens,
                    usage.CompletionTokens,
                    usage.TotalTokens);
            }

            if (!llmResponse.IsSuccess)
            {
                var statusCode = IsTemporaryLlmFailure(llmResponse.StatusCode)
                    ? llmResponse.StatusCode!.Value
                    : 502;
                logger.LogWarning(
                    "LLM failure for session {SessionId} (status {StatusCode}): {ErrorMessage}",
                    sessionId,
                    statusCode,
                    llmResponse.ErrorMessage);

                return ServiceResult<ConversationMessageResponse>.Failure(
                    llmResponse.ErrorMessage ?? "Gemini request failed.",
                    statusCode,
                    new ConversationFailureDetails(ToResponse(ctx.UserMessage), appliedMemoryChanges));
            }

            var functionCalls = llmResponse.FunctionCalls ?? [];
            if (functionCalls.Count == 0)
            {
                break;
            }

            logger.LogInformation(
                "Tool calling session {SessionId} iteration {Iteration}: {FunctionCallCount} function call(s).",
                sessionId,
                iteration,
                functionCalls.Count);
            workingMessages.Add(LLMMessage.ForFunctionCalls(functionCalls));
            var toolResponses = new List<LLMFunctionResponse>();
            foreach (var functionCall in functionCalls)
            {
                var argsText = functionCall.Args.GetRawText();
                var toolResponse = ShouldRejectSaveBeforeRead(functionCall, readMemoryKeys, out var guardError)
                    ? CreateToolErrorResponse(functionCall.Name, guardError)
                    : await memoryToolHandler.HandleAsync(
                        ctx.Session.TutorId,
                        functionCall.Name,
                        functionCall.Args,
                        ctx.UserMessage.Id,
                        cancellationToken);
                var responseForConversation = toolResponse with { Id = functionCall.Id };
                var responseText = responseForConversation.Response.GetRawText();
                toolAttempts.Add($"iteration={iteration}; tool={functionCall.Name}; args={argsText}; response={responseText}");
                toolResponses.Add(responseForConversation);

                ProcessToolOutcome(
                    functionCall,
                    responseForConversation,
                    responseText,
                    readMemoryKeys,
                    appliedMemoryChanges,
                    ref lastSaveMemoryFailed,
                    ref lastSaveMemoryFailure);
            }

            workingMessages.Add(LLMMessage.ForFunctionResponses(toolResponses));
        }

        if (llmResponse is null || (llmResponse.FunctionCalls?.Count ?? 0) > 0)
        {
            logger.LogWarning(
                "Tool calling iteration limit ({MaxToolIterations}) reached without final assistant response for session {SessionId}. Attempts: {Attempts}",
                MaxToolIterations,
                sessionId,
                string.Join(" | ", toolAttempts));

            return ServiceResult<ConversationMessageResponse>.Failure(
                "El tutor alcanzó el límite de operaciones internas de memoria sin generar respuesta final. Reintenta este mensaje.",
                502,
                new ConversationFailureDetails(ToResponse(ctx.UserMessage), appliedMemoryChanges));
        }

        if (totalPromptTokens > 0 || totalCompletionTokens > 0)
        {
            logger.LogInformation(
                "Completed turn for session {SessionId}: totalPromptTokens={TotalPromptTokens}, totalCompletionTokens={TotalCompletionTokens}",
                sessionId,
                totalPromptTokens,
                totalCompletionTokens);
        }

        var assistantMessage = await PersistAssistantMessageAsync(ctx.Session, llmResponse.Content ?? string.Empty, cancellationToken);

        return ServiceResult<ConversationMessageResponse>.Success(
            new ConversationMessageResponse(ToResponse(ctx.UserMessage), ToResponse(assistantMessage), appliedMemoryChanges));
    }

    public async IAsyncEnumerable<ConversationStreamEvent> StreamMessageAsync(
        int sessionId,
        CreateConversationMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prep = await PrepareTurnContextAsync(sessionId, request, cancellationToken);
        if (!prep.IsSuccess)
        {
            yield return new ConversationStreamEvent(
                "error",
                new StreamErrorPayload(prep.StatusCode ?? 400, prep.ErrorMessage ?? "Error de validación."));
            yield break;
        }

        var ctx = prep.Data!;
        var userResponse = ToResponse(ctx.UserMessage);
        yield return new ConversationStreamEvent("user_message", userResponse);

        var workingMessages = ctx.PreviousMessages.ToList();
        workingMessages.Add(new LLMMessage("user", ctx.UserMessage.Content));

        var toolAttempts = new List<string>();
        var appliedMemoryChanges = new List<AppliedMemoryChangeSummary>();
        var readMemoryKeys = new HashSet<string>(StringComparer.Ordinal);
        var lastSaveMemoryFailed = false;
        string? lastSaveMemoryFailure = null;
        var finalAssistantBuilder = new StringBuilder();
        var completedFinalAnswer = false;

        for (var iteration = 1; iteration <= MaxToolIterations; iteration++)
        {
            var messagesForRequest = BuildMessagesForIteration(workingMessages, lastSaveMemoryFailed, lastSaveMemoryFailure);
            var iterationTextBuilder = new StringBuilder();
            IReadOnlyList<LLMFunctionCall>? iterationFunctionCalls = null;

            await foreach (var chunk in llmProviderRouter.StreamGenerateAsync(
                ctx.Session.Tutor.LlmProfile,
                new PromptRequest(ctx.Session.Tutor.GeminiModel, ctx.SystemPrompt, messagesForRequest, MemoryToolDeclarations.All),
                cancellationToken))
            {
                if (chunk.Usage is { } usage)
                {
                    logger.LogInformation(
                        "LLM stream token usage session {SessionId} iteration {Iteration}: promptTokens={PromptTokens}, completionTokens={CompletionTokens}, totalTokens={TotalTokens}",
                        sessionId,
                        iteration,
                        usage.PromptTokens,
                        usage.CompletionTokens,
                        usage.TotalTokens);
                }

                if (chunk.IsError)
                {
                    var statusCode = IsTemporaryLlmFailure(chunk.StatusCode)
                        ? chunk.StatusCode!.Value
                        : 502;
                    logger.LogWarning(
                        "LLM stream failure for session {SessionId} (status {StatusCode}): {ErrorMessage}",
                        sessionId,
                        statusCode,
                        chunk.ErrorMessage);

                    yield return new ConversationStreamEvent(
                        "error",
                        new StreamErrorPayload(
                            statusCode,
                            chunk.ErrorMessage ?? "Gemini stream failed.",
                            userResponse,
                            appliedMemoryChanges));
                    yield break;
                }

                if (chunk.FunctionCalls is { Count: > 0 } calls)
                {
                    iterationFunctionCalls = calls;
                }

                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    iterationTextBuilder.Append(chunk.TextDelta);
                    yield return new ConversationStreamEvent(
                        "token_delta",
                        new StreamTokenDeltaPayload(chunk.TextDelta));
                }
            }

            if (iterationFunctionCalls is null || iterationFunctionCalls.Count == 0)
            {
                finalAssistantBuilder.Append(iterationTextBuilder);
                completedFinalAnswer = true;
                break;
            }

            workingMessages.Add(LLMMessage.ForFunctionCalls(iterationFunctionCalls));
            var toolResponses = new List<LLMFunctionResponse>();
            foreach (var functionCall in iterationFunctionCalls)
            {
                var (toolKey, toolOp) = ExtractToolMetadata(functionCall);
                yield return new ConversationStreamEvent(
                    "tool_call_started",
                    new StreamToolActivityPayload(iteration, functionCall.Name, toolKey, toolOp));

                var argsText = functionCall.Args.GetRawText();
                var toolResponse = ShouldRejectSaveBeforeRead(functionCall, readMemoryKeys, out var guardError)
                    ? CreateToolErrorResponse(functionCall.Name, guardError)
                    : await memoryToolHandler.HandleAsync(
                        ctx.Session.TutorId,
                        functionCall.Name,
                        functionCall.Args,
                        ctx.UserMessage.Id,
                        cancellationToken);

                var responseForConversation = toolResponse with { Id = functionCall.Id };
                var responseText = responseForConversation.Response.GetRawText();
                var succeeded = ToolSucceeded(responseForConversation.Response);
                toolAttempts.Add($"iteration={iteration}; tool={functionCall.Name}; args={argsText}; response={responseText}");
                toolResponses.Add(responseForConversation);

                ProcessToolOutcome(
                    functionCall,
                    responseForConversation,
                    responseText,
                    readMemoryKeys,
                    appliedMemoryChanges,
                    ref lastSaveMemoryFailed,
                    ref lastSaveMemoryFailure);

                yield return new ConversationStreamEvent(
                    "tool_call_completed",
                    new StreamToolActivityPayload(iteration, functionCall.Name, toolKey, toolOp, succeeded));
            }

            workingMessages.Add(LLMMessage.ForFunctionResponses(toolResponses));
        }

        if (!completedFinalAnswer)
        {
            logger.LogWarning(
                "Stream tool calling iteration limit ({MaxToolIterations}) reached without final assistant response for session {SessionId}. Attempts: {Attempts}",
                MaxToolIterations,
                sessionId,
                string.Join(" | ", toolAttempts));

            yield return new ConversationStreamEvent(
                "error",
                new StreamErrorPayload(
                    502,
                    "El tutor alcanzó el límite de operaciones internas de memoria sin generar respuesta final. Reintenta este mensaje.",
                    userResponse,
                    appliedMemoryChanges));
            yield break;
        }

        var assistantMessage = await PersistAssistantMessageAsync(
            ctx.Session,
            finalAssistantBuilder.ToString(),
            cancellationToken);

        yield return new ConversationStreamEvent(
            "turn_completed",
            new ConversationMessageResponse(userResponse, ToResponse(assistantMessage), appliedMemoryChanges));
    }

    private async Task<ServiceResult<PreparedTurnContext>> PrepareTurnContextAsync(
        int sessionId,
        CreateConversationMessageRequest request,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.StudySessions
            .Include(studySession => studySession.Tutor)
            .FirstOrDefaultAsync(studySession => studySession.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PreparedTurnContext>.Failure("Not found", 404);
        }

        Message userMessage;
        IQueryable<Message> historyQuery = dbContext.Messages
            .AsNoTracking()
            .Where(message => message.SessionId == sessionId);

        if (request.RetryUserMessageId.HasValue)
        {
            var existingMessage = await dbContext.Messages
                .FirstOrDefaultAsync(
                    message => message.Id == request.RetryUserMessageId.Value && message.SessionId == sessionId,
                    cancellationToken);
            if (existingMessage is null)
            {
                return ServiceResult<PreparedTurnContext>.Failure(
                    "El mensaje a reintentar no existe en esta sesión.",
                    404);
            }

            if (existingMessage.Role != MessageRole.User)
            {
                return ServiceResult<PreparedTurnContext>.Failure(
                    "Solo se pueden reintentar mensajes del usuario.",
                    400);
            }

            var hasNewerMessages = await dbContext.Messages
                .AsNoTracking()
                .AnyAsync(message => message.SessionId == sessionId && message.Id > existingMessage.Id, cancellationToken);
            if (hasNewerMessages)
            {
                return ServiceResult<PreparedTurnContext>.Failure(
                    "Solo se puede reintentar el último mensaje sin respuesta de la sesión.",
                    409);
            }

            userMessage = existingMessage;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            historyQuery = historyQuery.Where(message => message.Id < userMessage.Id);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return ServiceResult<PreparedTurnContext>.Failure(
                    "El contenido del mensaje es obligatorio.",
                    400);
            }
        }

        var totalMessageCount = await historyQuery.CountAsync(cancellationToken);

        var recentMessages = await historyQuery
            .OrderByDescending(message => message.Id)
            .Take(MaxHistoryMessages)
            .Select(message => new LLMMessage(message.Role.ToString().ToLowerInvariant(), message.Content, null, null, null, null))
            .ToListAsync(cancellationToken);
        recentMessages.Reverse();
        var previousMessages = recentMessages;

        if (totalMessageCount > MaxHistoryMessages)
        {
            var omitted = totalMessageCount - MaxHistoryMessages;
            var summary = new LLMMessage("system",
                $"[Contexto resumido: {omitted} mensajes antiguos omitidos para optimizar. Mostrando los {MaxHistoryMessages} más recientes. Si necesitas detalle histórico, consulta la auditoría.]");
            previousMessages = new[] { summary }.Concat(previousMessages).ToList();
            logger.LogInformation(
                "Session {SessionId}: history truncated from {TotalMessages} to {MaxHistoryMessages} (+1 summary) for LLM context.",
                sessionId,
                totalMessageCount,
                MaxHistoryMessages);
        }

        if (!request.RetryUserMessageId.HasValue)
        {
            var now = DateTime.UtcNow;
            userMessage = new Message
            {
                SessionId = sessionId,
                Role = MessageRole.User,
                Content = request.Content!.Trim(),
                CreatedAtUtc = now
            };

            session.UpdatedAtUtc = now;
            dbContext.Messages.Add(userMessage);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            userMessage = await dbContext.Messages
                .AsNoTracking()
                .SingleAsync(m => m.Id == request.RetryUserMessageId.Value, cancellationToken);
        }

        var systemPrompt = await promptBuilder.BuildSystemPromptAsync(
            session.TutorId,
            request.Profile,
            session.Goal,
            cancellationToken);

        var estimatedInputChars = systemPrompt.Length + previousMessages.Sum(m => m.Content?.Length ?? 0) + userMessage.Content.Length;
        logger.LogInformation(
            "Prompt metrics session {SessionId}: systemPromptChars={SystemPromptChars}, historyCount={HistoryCount}/{TotalMessages}, estimatedInputChars={EstimatedInputChars}, estimatedTokens={EstimatedTokens}, profile={Profile}",
            sessionId,
            systemPrompt.Length,
            previousMessages.Count,
            totalMessageCount,
            estimatedInputChars,
            estimatedInputChars / 4,
            request.Profile);

        return ServiceResult<PreparedTurnContext>.Success(
            new PreparedTurnContext(session, userMessage, previousMessages, systemPrompt));
    }

    private async Task<Message> PersistAssistantMessageAsync(
        StudySession session,
        string content,
        CancellationToken cancellationToken)
    {
        var assistantTime = DateTime.UtcNow;
        var assistantMessage = new Message
        {
            SessionId = session.Id,
            Role = MessageRole.Assistant,
            Content = content,
            CreatedAtUtc = assistantTime
        };

        session.UpdatedAtUtc = assistantTime;
        dbContext.Messages.Add(assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);
        return assistantMessage;
    }

    private static IReadOnlyList<LLMMessage> BuildMessagesForIteration(
        List<LLMMessage> workingMessages,
        bool lastSaveMemoryFailed,
        string? lastSaveMemoryFailure) =>
        lastSaveMemoryFailed
            ? workingMessages.Append(new LLMMessage(
                "system",
                $"NOTA INTERNA: el intento de guardar memoria en este turno falló y no se corrigió. Tu respuesta final al usuario debe reflejar esto honestamente; no afirmes que el progreso o nivel se actualizó si la herramienta no tuvo éxito. Detalle del fallo: {lastSaveMemoryFailure}"))
                .ToList()
            : workingMessages;

    private static void ProcessToolOutcome(
        LLMFunctionCall functionCall,
        LLMFunctionResponse responseForConversation,
        string responseText,
        HashSet<string> readMemoryKeys,
        List<AppliedMemoryChangeSummary> appliedMemoryChanges,
        ref bool lastSaveMemoryFailed,
        ref string? lastSaveMemoryFailure)
    {
        TrackReadMemory(functionCall, responseForConversation, readMemoryKeys);
        if (functionCall.Name != "guardar_memoria")
        {
            return;
        }

        if (ToolSucceeded(responseForConversation.Response))
        {
            lastSaveMemoryFailed = false;
            lastSaveMemoryFailure = null;
            if (TryExtractAppliedChange(responseForConversation.Response, out var appliedChange))
            {
                appliedMemoryChanges.Add(appliedChange);
            }
        }
        else
        {
            lastSaveMemoryFailed = true;
            lastSaveMemoryFailure = responseText;
        }
    }

    private static (string? Key, string? Operation) ExtractToolMetadata(LLMFunctionCall functionCall)
    {
        if (functionCall.Name == "leer_memoria"
            && functionCall.Args.TryGetProperty("key", out var readKey)
            && readKey.ValueKind == JsonValueKind.String)
        {
            return (readKey.GetString(), null);
        }

        if (functionCall.Name == "guardar_memoria"
            && TryGetPatchKeyAndOperation(functionCall.Args, out var patchKey, out var patchOp))
        {
            return (patchKey, patchOp);
        }

        return (null, null);
    }

    private static bool TryExtractAppliedChange(JsonElement response, out AppliedMemoryChangeSummary summary)
    {
        summary = default!;
        if (!response.TryGetProperty("memoryChangeId", out var idEl) || !idEl.TryGetInt32(out var memoryChangeId)
            || !response.TryGetProperty("key", out var keyEl) || keyEl.ValueKind != JsonValueKind.String
            || !response.TryGetProperty("operation", out var opEl) || opEl.ValueKind != JsonValueKind.String
            || !response.TryGetProperty("path", out var pathEl) || pathEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var targetId = response.TryGetProperty("targetId", out var targetEl) && targetEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(targetEl.GetString())
            ? targetEl.GetString()
            : null;
        var reason = response.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
            ? reasonEl.GetString() ?? string.Empty
            : string.Empty;

        summary = new AppliedMemoryChangeSummary(
            memoryChangeId,
            keyEl.GetString()!,
            opEl.GetString()!,
            pathEl.GetString()!,
            targetId,
            reason);
        return true;
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

        if (operation is not ("Update" or "Resolve") || key is not (MemoryKeys.DomainMap or MemoryKeys.GapsOrErrors or MemoryKeys.Roadmap))
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

    private static bool IsTemporaryLlmFailure(int? statusCode) =>
        statusCode is 429 or 503;

    private static MessageResponse ToResponse(Message message) => new(
        message.Id,
        message.SessionId,
        message.Role.ToString().ToLowerInvariant(),
        message.Content,
        message.CreatedAtUtc);

    private sealed record PreparedTurnContext(
        StudySession Session,
        Message UserMessage,
        IReadOnlyList<LLMMessage> PreviousMessages,
        string SystemPrompt);
}
