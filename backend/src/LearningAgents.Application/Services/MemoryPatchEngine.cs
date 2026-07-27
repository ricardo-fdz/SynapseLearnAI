using System.Text.Json;
using System.Text.Json.Nodes;
using LearningAgents.Application.Dtos.MemoryChanges;
using LearningAgents.Application.Dtos.MemoryEntries;
using LearningAgents.Application.Dtos.MemoryPatches;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Enums;
using LearningAgents.Domain.Memory;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LearningAgents.Application.Services;

internal sealed class MemoryPatchEngine(LearningAgentsDbContext dbContext) : IMemoryPatchEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly HashSet<string> SessionMemoryFields =
    [
        "fecha_ultima_sesion",
        "nivel_actual",
        "temas_dominados_ultima_sesion",
        "ultimo_ejercicio",
        "tiempo_invertido_minutos",
        "proximo_paso"
    ];

    private static readonly HashSet<string> StudentProfileRootFields =
    [
        "alias",
        "lenguaje_principal",
        "objetivo_declarado",
        "estilo_aprendizaje",
        "preferencias_comunicacion",
        "notas_tutor",
        "diagnostico_nivel",
        "ultima_actualizacion"
    ];

    private static readonly HashSet<string> StudentLearningStyleFields =
    ["prefiere", "ritmo_sesion", "reaccion_ante_errores", "nivel_autonomia"];

    private static readonly HashSet<string> StudentCommunicationFields = ["idioma", "tono_tutor"];

    private static readonly HashSet<string> DomainTopicFields =
    ["id", "nombre", "nivel", "ultima_evaluacion", "notas"];

    public async Task<MemoryPatchResult> ApplyPatchAsync(
        int tutorId,
        MemoryPatch patch,
        int? messageId,
        CancellationToken cancellationToken = default)
    {
        ValidateCommonPatch(patch);

        var entry = await dbContext.MemoryEntries
            .FirstOrDefaultAsync(memoryEntry => memoryEntry.TutorId == tutorId && memoryEntry.Key == patch.Key, cancellationToken)
            ?? throw new InvalidMemoryPatchException($"MemoryEntry '{patch.Key}' does not exist for tutor {tutorId}.");

        var previousValueJson = entry.ValueJson;
        var root = ParseObject(previousValueJson, patch.Key);
        var effectiveReason = ApplySpecificPatch(root, patch);
        var newValueJson = root.ToJsonString(JsonOptions);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        entry.ValueJson = newValueJson;
        entry.UpdatedAtUtc = DateTime.UtcNow;

        var change = new MemoryChange
        {
            MemoryEntryId = entry.Id,
            MessageId = messageId,
            Operation = patch.Operation,
            Path = patch.Path,
            TargetId = patch.TargetId ?? string.Empty,
            PreviousValueJson = previousValueJson,
            NewValueJson = newValueJson,
            Reason = effectiveReason,
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.MemoryChanges.Add(change);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new MemoryPatchResult(ToMemoryEntryResponse(entry), ToMemoryChangeResponse(change));
    }

    private static void ValidateCommonPatch(MemoryPatch patch)
    {
        if (!MemoryKeys.IsStandard(patch.Key))
        {
            throw new InvalidMemoryPatchException($"Memory key '{patch.Key}' is not a standard memory key.");
        }

        if (string.IsNullOrWhiteSpace(patch.Path) || !patch.Path.StartsWith('/'))
        {
            if (patch.Key == MemoryKeys.DomainMap && patch.Operation == MemoryPatchOperation.Update)
            {
                throw new InvalidMemoryPatchException(DomainMapUpdatePathError);
            }

            throw new InvalidMemoryPatchException("Patch path must start with '/'.");
        }

        if (string.IsNullOrWhiteSpace(patch.Reason))
        {
            throw new InvalidMemoryPatchException("Patch reason is required.");
        }

        if ((patch.Operation == MemoryPatchOperation.Update || patch.Operation == MemoryPatchOperation.Resolve)
            && string.IsNullOrWhiteSpace(patch.TargetId))
        {
            throw new InvalidMemoryPatchException(
                $"TargetId is required for {patch.Operation} operations. When updating or resolving an item inside an array, targetId must be the existing item's 'id' field, not its visible name or title. Use leer_memoria with key='{patch.Key}' first to obtain the valid ids.");
        }
    }

    private static JsonObject ParseObject(string valueJson, string key)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(valueJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidMemoryPatchException($"MemoryEntry '{key}' contains invalid JSON: {exception.Message}");
        }

        return node as JsonObject
            ?? throw new InvalidMemoryPatchException($"MemoryEntry '{key}' must contain a JSON object.");
    }

    private static string ApplySpecificPatch(JsonObject root, MemoryPatch patch)
    {
        return patch.Key switch
        {
            MemoryKeys.SessionMemory => ApplySessionMemoryPatch(root, patch),
            MemoryKeys.StudentProfile => ApplyStudentProfilePatch(root, patch),
            MemoryKeys.DomainMap => ApplyDomainMapPatch(root, patch),
            MemoryKeys.GapsOrErrors => ApplyGapsOrErrorsPatch(root, patch),
            MemoryKeys.ActivityHistory => ApplyActivityHistoryPatch(root, patch),
            _ => throw new InvalidMemoryPatchException($"Memory key '{patch.Key}' is not supported.")
        };
    }

    private static string ApplySessionMemoryPatch(JsonObject root, MemoryPatch patch)
    {
        if (patch.Operation != MemoryPatchOperation.Set)
        {
            throw new InvalidMemoryPatchException("memoria_sesion only supports Set operations.");
        }

        var field = SingleSegment(patch.Path, "memoria_sesion supports paths like '/proximo_paso'.");
        if (!SessionMemoryFields.Contains(field))
        {
            throw new InvalidMemoryPatchException($"Path '{patch.Path}' is not a known memoria_sesion field.");
        }

        root[field] = CloneValue(patch.Value);
        return patch.Reason;
    }

    private static string ApplyStudentProfilePatch(JsonObject root, MemoryPatch patch)
    {
        if (patch.Operation == MemoryPatchOperation.Resolve)
        {
            throw new InvalidMemoryPatchException("perfil_estudiante does not support Resolve operations.");
        }

        if (patch.Operation == MemoryPatchOperation.Add)
        {
            if (patch.Path != "/notas_tutor")
            {
                throw new InvalidMemoryPatchException("perfil_estudiante Add only supports path '/notas_tutor'.");
            }

            var note = RequireObjectValue(patch.Value, "perfil_estudiante Add requires an object value.");
            RequireString(note, "fecha", "perfil_estudiante note requires 'fecha'.");
            RequireString(note, "nota", "perfil_estudiante note requires 'nota'.");
            GetOrCreateArray(root, "notas_tutor").Add(CloneValue(patch.Value));
            return patch.Reason;
        }

        if (patch.Operation is not (MemoryPatchOperation.Set or MemoryPatchOperation.Update))
        {
            throw new InvalidMemoryPatchException($"perfil_estudiante does not support {patch.Operation} operations.");
        }

        SetStudentProfilePath(root, patch.Path, patch.Value);
        return patch.Reason;
    }

    private static void SetStudentProfilePath(JsonObject root, string path, JsonElement value)
    {
        var segments = PathSegments(path);
        if (segments.Length == 1)
        {
            if (!StudentProfileRootFields.Contains(segments[0]) || segments[0] == "notas_tutor")
            {
                throw new InvalidMemoryPatchException($"Path '{path}' is not a settable perfil_estudiante field.");
            }

            root[segments[0]] = CloneValue(value);
            return;
        }

        if (segments.Length != 2)
        {
            throw new InvalidMemoryPatchException("perfil_estudiante supports root paths or one nested object segment.");
        }

        var allowedFields = segments[0] switch
        {
            "estilo_aprendizaje" => StudentLearningStyleFields,
            "preferencias_comunicacion" => StudentCommunicationFields,
            _ => throw new InvalidMemoryPatchException($"Path '{path}' is not a known perfil_estudiante nested object.")
        };

        if (!allowedFields.Contains(segments[1]))
        {
            throw new InvalidMemoryPatchException($"Path '{path}' is not a known perfil_estudiante nested field.");
        }

        var parent = GetOrCreateObject(root, segments[0]);
        parent[segments[1]] = CloneValue(value);
    }

    private static string ApplyDomainMapPatch(JsonObject root, MemoryPatch patch)
    {
        if (patch.Operation == MemoryPatchOperation.Add)
        {
            if (patch.Path != "/temas")
            {
                throw new InvalidMemoryPatchException("mapa_dominio Add only supports path '/temas'.");
            }

            var topic = RequireObjectValue(patch.Value, "mapa_dominio Add requires an object value.");
            var topicId = RequireString(topic, "id", "mapa_dominio topic requires 'id'.");
            var topicName = TryGetString(topic, "nombre") ?? GenerateNameFromId(topicId);
            if (topicName is null)
            {
                return patch.Reason + " [no-op: no se pudo determinar 'nombre' para el tema]";
            }
            topic["nombre"] = topicName;
            RequireProperty(topic, "nivel", "mapa_dominio topic requires 'nivel'.");
            var topics = GetRequiredArray(root, "temas", "mapa_dominio requires a 'temas' array.");

            var existing = FindByIdOrNull(topics, topicId);
            if (existing is not null)
            {
                MergeObjectFields(existing, topic, overwriteId: true);
                return patch.Reason + " [upsert: elemento ya existía, actualizado en lugar de duplicado]";
            }

            var existingByName = FindByNormalizedStringPropertyOrNull(topics, "nombre", topicName);
            if (existingByName is not null)
            {
                MergeObjectFields(existingByName, topic, overwriteId: false);
                return patch.Reason + " [upsert: tema con mismo nombre ya existía, actualizado en lugar de duplicado]";
            }

            topics.Add(topic);
            return patch.Reason;
        }

        if (patch.Operation == MemoryPatchOperation.Update)
        {
            var field = TwoSegmentField(patch.Path, "temas", DomainMapUpdatePathError);
            if (!DomainTopicFields.Contains(field) || field == "id")
            {
                throw new InvalidMemoryPatchException(DomainMapUpdatePathError);
            }

            var topic = FindById(
                GetRequiredArray(root, "temas", "mapa_dominio requires a 'temas' array."),
                patch.TargetId!,
                "mapa_dominio topic",
                "targetId must be the 'id' field of an existing tema in mapa_dominio, not its 'nombre'. Use leer_memoria with key='mapa_dominio' first to obtain the valid tema ids.");
            topic[field] = CloneValue(patch.Value);
            return patch.Reason;
        }

        throw new InvalidMemoryPatchException("mapa_dominio only supports Add and Update operations.");
    }

    private const string DomainMapUpdatePathError =
        "El path debe apuntar a UN campo específico del tema, con el formato '/temas/nombreDelCampo' (ej. '/temas/nivel' o '/temas/notas'), siempre empezando con '/'. No puedes actualizar varios campos en una sola llamada; si necesitas actualizar tanto 'nivel' como 'notas', haz DOS llamadas a guardar_memoria, una por campo. ACCIÓN REQUERIDA AHORA: corrige el path y vuelve a llamar guardar_memoria con un solo campo a la vez, antes de responder al usuario.";

    private static string ApplyGapsOrErrorsPatch(JsonObject root, MemoryPatch patch)
    {
        if (patch.Operation == MemoryPatchOperation.Add)
        {
            if (patch.Path != "/activas")
            {
                throw new InvalidMemoryPatchException("lagunas_o_errores Add only supports path '/activas'.");
            }

            var gap = RequireObjectValue(patch.Value, "lagunas_o_errores Add requires an object value.");
            var gapId = RequireString(gap, "id", "lagunas_o_errores active gap requires 'id'.");
            RequireString(gap, "concepto", "lagunas_o_errores active gap requires 'concepto'.");
            RequireString(gap, "descripcion", "lagunas_o_errores active gap requires 'descripcion'.");
            var activeGaps = GetRequiredArray(root, "activas", "lagunas_o_errores requires an 'activas' array.");

            if (FindByIdOrNull(activeGaps, gapId) is not null)
            {
                return patch.Reason + " [no-op: elemento ya existía, no se duplicó]";
            }

            activeGaps.Add(CloneValue(patch.Value));
            return patch.Reason;
        }

        if (patch.Operation == MemoryPatchOperation.Resolve)
        {
            if (patch.Path != "/activas")
            {
                throw new InvalidMemoryPatchException("lagunas_o_errores Resolve only supports path '/activas'.");
            }

            var value = RequireObjectValue(patch.Value, "lagunas_o_errores Resolve requires an object value.");
            var resolutionDate = RequireString(value, "fecha_resolucion", "lagunas_o_errores Resolve requires 'fecha_resolucion'.");
            var resolution = RequireString(value, "como_se_resolvio", "lagunas_o_errores Resolve requires 'como_se_resolvio'.");
            var activeGaps = GetRequiredArray(root, "activas", "lagunas_o_errores requires an 'activas' array.");
            var resolvedGaps = GetRequiredArray(root, "resueltas", "lagunas_o_errores requires a 'resueltas' array.");
            var index = FindIndexById(
                activeGaps,
                patch.TargetId!,
                "active laguna",
                "targetId must be the 'id' field of an existing active laguna in lagunas_o_errores, not its 'concepto' or visible title. Use leer_memoria with key='lagunas_o_errores' first to obtain the valid active laguna ids.");
            var resolvedGap = activeGaps[index]!.AsObject().DeepClone().AsObject();
            activeGaps.RemoveAt(index);
            resolvedGap["fecha_resolucion"] = resolutionDate;
            resolvedGap["como_se_resolvio"] = resolution;
            resolvedGaps.Add(resolvedGap);
            return patch.Reason;
        }

        throw new InvalidMemoryPatchException("lagunas_o_errores only supports Add and Resolve operations.");
    }

    private static string ApplyActivityHistoryPatch(JsonObject root, MemoryPatch patch)
    {
        if (patch.Operation != MemoryPatchOperation.Add)
        {
            throw new InvalidMemoryPatchException("historial_actividades only supports Add operations.");
        }

        if (patch.Path != "/proyectos")
        {
            throw new InvalidMemoryPatchException("historial_actividades Add only supports path '/proyectos'.");
        }

        var project = RequireObjectValue(patch.Value, "historial_actividades Add requires an object value.");
        var projectId = RequireString(project, "id", "historial_actividades project requires 'id'.");
        RequireString(project, "nombre", "historial_actividades project requires 'nombre'.");
        RequireString(project, "resultado", "historial_actividades project requires 'resultado'.");
        var projects = GetRequiredArray(root, "proyectos", "historial_actividades requires a 'proyectos' array.");

        if (FindByIdOrNull(projects, projectId) is not null)
        {
            return patch.Reason + " [no-op: elemento ya existía, no se duplicó]";
        }

        projects.Add(CloneValue(patch.Value));
        return patch.Reason;
    }

    private static string SingleSegment(string path, string errorMessage)
    {
        var segments = PathSegments(path);
        if (segments.Length != 1)
        {
            throw new InvalidMemoryPatchException(errorMessage);
        }

        return segments[0];
    }

    private static string TwoSegmentField(string path, string expectedRoot, string errorMessage)
    {
        var segments = PathSegments(path);
        if (segments.Length != 2 || segments[0] != expectedRoot)
        {
            throw new InvalidMemoryPatchException(errorMessage);
        }

        return segments[1];
    }

    private static string[] PathSegments(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static JsonObject GetOrCreateObject(JsonObject root, string propertyName)
    {
        if (root[propertyName] is null)
        {
            root[propertyName] = new JsonObject();
        }

        return root[propertyName] as JsonObject
            ?? throw new InvalidMemoryPatchException($"Property '{propertyName}' must be an object.");
    }

    private static JsonArray GetOrCreateArray(JsonObject root, string propertyName)
    {
        if (root[propertyName] is null)
        {
            root[propertyName] = new JsonArray();
        }

        return root[propertyName] as JsonArray
            ?? throw new InvalidMemoryPatchException($"Property '{propertyName}' must be an array.");
    }

    private static JsonArray GetRequiredArray(JsonObject root, string propertyName, string errorMessage) =>
        root[propertyName] as JsonArray ?? throw new InvalidMemoryPatchException(errorMessage);

    private static JsonObject FindById(JsonArray array, string targetId, string label, string targetIdGuidance)
    {
        foreach (var item in array)
        {
            if (item is JsonObject itemObject && string.Equals(itemObject["id"]?.GetValue<string>(), targetId, StringComparison.Ordinal))
            {
                return itemObject;
            }
        }

        throw new InvalidMemoryPatchException($"TargetId '{targetId}' was not found in {label} array. {targetIdGuidance}");
    }

    private static JsonObject? FindByIdOrNull(JsonArray array, string id)
    {
        foreach (var item in array)
        {
            if (item is JsonObject itemObject && string.Equals(itemObject["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
            {
                return itemObject;
            }
        }
        return null;
    }

    private static JsonObject? FindByNormalizedStringPropertyOrNull(JsonArray array, string propertyName, string value)
    {
        var normalizedValue = NormalizeText(value);
        foreach (var item in array)
        {
            if (item is JsonObject itemObject
                && itemObject[propertyName] is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var itemValue)
                && string.Equals(NormalizeText(itemValue), normalizedValue, StringComparison.Ordinal))
            {
                return itemObject;
            }
        }

        return null;
    }

    private static void MergeObjectFields(JsonObject target, JsonObject source, bool overwriteId)
    {
        foreach (var kvp in source)
        {
            if (!overwriteId && kvp.Key == "id")
            {
                continue;
            }

            target[kvp.Key] = kvp.Value?.DeepClone();
        }
    }

    private static string NormalizeText(string value) => value.Trim().ToUpperInvariant();

    private static int FindIndexById(JsonArray array, string targetId, string label, string targetIdGuidance)
    {
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is JsonObject itemObject && string.Equals(itemObject["id"]?.GetValue<string>(), targetId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        throw new InvalidMemoryPatchException($"TargetId '{targetId}' was not found in {label} array. {targetIdGuidance}");
    }

    private static JsonObject RequireObjectValue(JsonElement value, string errorMessage) =>
        CloneValue(value) as JsonObject ?? throw new InvalidMemoryPatchException(errorMessage);

    private static void RequireProperty(JsonObject value, string propertyName, string errorMessage)
    {
        if (!value.ContainsKey(propertyName) || value[propertyName] is null)
        {
            throw new InvalidMemoryPatchException(errorMessage);
        }
    }

    private static string RequireString(JsonObject value, string propertyName, string errorMessage)
    {
        if (value[propertyName] is not JsonValue jsonValue
            || !jsonValue.TryGetValue<string>(out var stringValue)
            || string.IsNullOrWhiteSpace(stringValue))
        {
            throw new InvalidMemoryPatchException(errorMessage);
        }

        return stringValue;
    }

    private static string? TryGetString(JsonObject value, string propertyName)
    {
        if (value[propertyName] is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var stringValue)
            && !string.IsNullOrWhiteSpace(stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private static string GenerateNameFromId(string id)
    {
        var name = id;
        if (name.StartsWith("tema-", StringComparison.OrdinalIgnoreCase))
        {
            name = name["tema-".Length..];
        }
        name = name.Replace('-', ' ');

        if (name.Length > 0)
        {
            name = char.ToUpper(name[0]) + name[1..];
        }

        return name;
    }

    private static JsonNode? CloneValue(JsonElement value) => JsonNode.Parse(value.GetRawText())?.DeepClone();

    private static MemoryEntryResponse ToMemoryEntryResponse(MemoryEntry entry) => new(
        entry.Id,
        entry.TutorId,
        entry.Key,
        entry.ValueJson,
        entry.SchemaVersion,
        entry.CreatedAtUtc,
        entry.UpdatedAtUtc);

    private static MemoryChangeResponse ToMemoryChangeResponse(MemoryChange change) => new(
        change.Id,
        change.MemoryEntryId,
        change.MessageId,
        change.Operation.ToString(),
        change.Path,
        change.TargetId,
        change.PreviousValueJson,
        change.NewValueJson,
        change.Reason,
        change.CreatedAtUtc);
}
