using System.Text.Json;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.LLM;
using LearningAgents.Domain.Memory;

namespace LearningAgents.Application.Services;

internal sealed class MemoryToolHandler(
    IMemoryEntryService memoryEntryService,
    IMemoryPatchEngine memoryPatchEngine) : IMemoryToolHandler
{
    private static readonly string[] StandardKeys =
    [
        MemoryKeys.SessionMemory,
        MemoryKeys.StudentProfile,
        MemoryKeys.DomainMap,
        MemoryKeys.GapsOrErrors,
        MemoryKeys.ActivityHistory
    ];

    public async Task<LLMFunctionResponse> HandleAsync(
        int tutorId,
        string toolName,
        JsonElement args,
        int? messageId,
        CancellationToken cancellationToken = default)
    {
        var result = toolName switch
        {
            "leer_memoria" => await ReadMemoryAsync(tutorId, args, cancellationToken),
            "guardar_memoria" => await SaveMemoryAsync(tutorId, args, messageId, cancellationToken),
            "listar_memoria" => await ListMemoryAsync(tutorId, cancellationToken),
            _ => new { success = false, error = $"Unknown tool '{toolName}'." }
        };

        return new LLMFunctionResponse(toolName, JsonSerializer.SerializeToElement(result));
    }

    private async Task<object> ReadMemoryAsync(int tutorId, JsonElement args, CancellationToken cancellationToken)
    {
        var key = RequireString(args, "key");
        if (!MemoryKeys.IsStandard(key))
        {
            return new { success = false, error = $"Memory key '{key}' is not standard." };
        }

        var entry = (await memoryEntryService.GetByTutorIdAndKeysAsync(tutorId, [key], cancellationToken)).FirstOrDefault();
        return entry is null
            ? new { success = false, key, error = $"Memory '{key}' does not exist for tutor {tutorId}." }
            : new { success = true, key, valueJson = entry.ValueJson };
    }

    private async Task<object> SaveMemoryAsync(int tutorId, JsonElement args, int? messageId, CancellationToken cancellationToken)
    {
        if (!args.TryGetProperty("patch", out var patchElement) || patchElement.ValueKind != JsonValueKind.Object)
        {
            return new { success = false, error = "guardar_memoria requires a 'patch' object." };
        }

        try
        {
            var patch = ParsePatch(patchElement);
            var result = await memoryPatchEngine.ApplyPatchAsync(tutorId, patch, messageId, cancellationToken);
            return new
            {
                success = true,
                memoryEntryId = result.MemoryEntry.Id,
                memoryChangeId = result.MemoryChange.Id,
                key = result.MemoryEntry.Key,
                operation = result.MemoryChange.Operation,
                path = result.MemoryChange.Path,
                targetId = result.MemoryChange.TargetId,
                reason = result.MemoryChange.Reason
            };
        }
        catch (InvalidMemoryPatchException exception)
        {
            return new { success = false, error = exception.Message };
        }
        catch (JsonException exception)
        {
            return new { success = false, error = $"Invalid patch JSON: {exception.Message}" };
        }
        catch (ArgumentException exception)
        {
            return new { success = false, error = exception.Message };
        }
    }

    private async Task<object> ListMemoryAsync(int tutorId, CancellationToken cancellationToken)
    {
        var entries = await memoryEntryService.GetByTutorIdAndKeysAsync(tutorId, StandardKeys, cancellationToken);
        var byKey = entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var items = StandardKeys.Select(key => new
        {
            key,
            exists = byKey.ContainsKey(key),
            isEmpty = !byKey.TryGetValue(key, out var entry) || IsEmptyJson(entry.ValueJson)
        }).ToArray();

        return new { success = true, memories = items };
    }

    private static MemoryPatch ParsePatch(JsonElement patchElement)
    {
        var key = RequireString(patchElement, "key");
        var operationText = RequireString(patchElement, "operation");
        if (!Enum.TryParse<MemoryPatchOperation>(operationText, ignoreCase: true, out var operation))
        {
            throw new ArgumentException($"Operation '{operationText}' is not valid.");
        }

        var path = RequireString(patchElement, "path");
        var targetId = patchElement.TryGetProperty("targetId", out var targetIdElement)
            && targetIdElement.ValueKind != JsonValueKind.Null
            ? targetIdElement.GetString()
            : null;
        if (!patchElement.TryGetProperty("value", out var value))
        {
            throw new ArgumentException("Patch requires 'value'.");
        }

        var reason = RequireString(patchElement, "reason");
        return new MemoryPatch(key, operation, path, targetId, JsonDocument.Parse(value.GetRawText()).RootElement.Clone(), reason);
    }

    private static string RequireString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new ArgumentException($"Argument '{propertyName}' is required.");
        }

        return property.GetString()!;
    }

    private static bool IsEmptyJson(string valueJson)
    {
        try
        {
            using var document = JsonDocument.Parse(valueJson);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => !document.RootElement.EnumerateObject().Any()
                    || document.RootElement.EnumerateObject().All(property => IsEmptyElement(property.Value)),
                JsonValueKind.Array => document.RootElement.GetArrayLength() == 0,
                JsonValueKind.String => string.IsNullOrWhiteSpace(document.RootElement.GetString()),
                JsonValueKind.Null => true,
                JsonValueKind.Undefined => true,
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsEmptyElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => !element.EnumerateObject().Any()
            || element.EnumerateObject().All(property => IsEmptyElement(property.Value)),
        JsonValueKind.Array => element.GetArrayLength() == 0,
        JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()),
        JsonValueKind.Null => true,
        JsonValueKind.Undefined => true,
        _ => false
    };
}
