using System.Text;
using System.Text.Json;
using LearningAgents.Application.Dtos.MemoryEntries;
using LearningAgents.Application.Enums;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Memory;

namespace LearningAgents.Application.Services;

internal sealed class PromptBuilder(
    ITutorService tutorService,
    IMemoryEntryService memoryEntryService) : IPromptBuilder
{
    private const string GlobalPromptFileName = "PROMPT_GLOBAL.md";
    private const string GlobalPromptFallback = "Actúa como un tutor de programación útil y claro.";

    private static readonly string[] MemoryRenderOrder =
    [
        MemoryKeys.StudentProfile,
        MemoryKeys.SessionMemory,
        MemoryKeys.DomainMap,
        MemoryKeys.GapsOrErrors,
        MemoryKeys.ActivityHistory
    ];

    public async Task<string> BuildSystemPromptAsync(
        int tutorId,
        ContextLoadProfile profile,
        CancellationToken cancellationToken = default)
    {
        var tutor = await tutorService.GetByIdAsync(tutorId, cancellationToken)
            ?? throw new KeyNotFoundException($"Tutor {tutorId} does not exist.");

        var requiredKeys = GetRequiredKeys(profile);
        var entries = requiredKeys.Count == 0
            ? []
            : await memoryEntryService.GetByTutorIdAndKeysAsync(tutorId, requiredKeys, cancellationToken);

        var entriesByKey = entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var builder = new StringBuilder();
        builder.AppendLine(ReadGlobalPrompt().Trim());
        builder.AppendLine();
        builder.AppendLine($"Fecha actual (UTC): {DateTime.UtcNow:yyyy-MM-dd}. Usa esta fecha para cualquier campo de fecha que guardes en memoria durante este turno; no inventes fechas pasadas o futuras.");
        builder.AppendLine();
        builder.AppendLine(tutor.SystemPromptContent.Trim());

        foreach (var key in MemoryRenderOrder.Where(requiredKeys.Contains))
        {
            if (!entriesByKey.TryGetValue(key, out var entry))
            {
                continue;
            }

            var renderedMemory = RenderMemory(entry, profile);
            if (string.IsNullOrWhiteSpace(renderedMemory))
            {
                continue;
            }

            builder.AppendLine();
            builder.AppendLine(renderedMemory.TrimEnd());
        }

        return builder.ToString().TrimEnd();
    }

    private static string ReadGlobalPrompt()
    {
        var path = Path.Combine(AppContext.BaseDirectory, GlobalPromptFileName);
        return File.Exists(path) ? File.ReadAllText(path) : GlobalPromptFallback;
    }

    private static IReadOnlyCollection<string> GetRequiredKeys(ContextLoadProfile profile) => profile switch
    {
        ContextLoadProfile.Minimal => [],
        ContextLoadProfile.Standard =>
        [
            MemoryKeys.StudentProfile,
            MemoryKeys.SessionMemory,
            MemoryKeys.DomainMap,
            MemoryKeys.GapsOrErrors
        ],
        ContextLoadProfile.Evaluation =>
        [
            MemoryKeys.StudentProfile,
            MemoryKeys.DomainMap,
            MemoryKeys.GapsOrErrors
        ],
        ContextLoadProfile.Project =>
        [
            MemoryKeys.StudentProfile,
            MemoryKeys.DomainMap,
            MemoryKeys.ActivityHistory
        ],
        ContextLoadProfile.FullReview =>
        [
            MemoryKeys.StudentProfile,
            MemoryKeys.SessionMemory,
            MemoryKeys.DomainMap,
            MemoryKeys.GapsOrErrors,
            MemoryKeys.ActivityHistory
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null)
    };

    private static string RenderMemory(MemoryEntryResponse entry, ContextLoadProfile profile)
    {
        using var document = JsonDocument.Parse(entry.ValueJson);
        var root = document.RootElement;

        return entry.Key switch
        {
            MemoryKeys.StudentProfile => RenderStudentProfile(root),
            MemoryKeys.SessionMemory => RenderSessionMemory(root),
            MemoryKeys.DomainMap => RenderDomainMap(root),
            MemoryKeys.GapsOrErrors => RenderGapsOrErrors(root, includeResolved: profile == ContextLoadProfile.FullReview),
            MemoryKeys.ActivityHistory => RenderActivityHistory(root),
            _ => string.Empty
        };
    }

    private static string RenderStudentProfile(JsonElement root)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Memoria: perfil_estudiante");
        var wrote = false;

        wrote |= AppendValue(builder, root, "alias", "Alias");
        wrote |= AppendValue(builder, root, "lenguaje_principal", "Lenguaje principal");
        wrote |= AppendValue(builder, root, "objetivo_declarado", "Objetivo declarado");
        wrote |= AppendValue(builder, root, "objetivo", "Objetivo");
        wrote |= AppendValue(builder, root, "objetivos", "Objetivos");
        wrote |= AppendValue(builder, root, "nivel", "Nivel");
        wrote |= AppendValue(builder, root, "nivel_general", "Nivel general");
        wrote |= AppendValue(builder, root, "estilo_aprendizaje", "Estilo de aprendizaje");
        wrote |= AppendValue(builder, root, "preferencias_comunicacion", "Preferencias de comunicacion");
        wrote |= AppendValue(builder, root, "ritmo", "Ritmo");
        wrote |= AppendValue(builder, root, "preferencias", "Preferencias");
        wrote |= AppendValue(builder, root, "diagnostico_nivel", "Diagnostico de nivel");
        wrote |= AppendValue(builder, root, "notas_tutor", "Notas del tutor");
        wrote |= AppendValue(builder, root, "notas", "Notas");
        wrote |= AppendValue(builder, root, "ultima_actualizacion", "Ultima actualizacion");

        if (!wrote)
        {
            builder.AppendLine("Sin perfil registrado.");
        }

        return builder.ToString();
    }

    private static string RenderSessionMemory(JsonElement root)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Memoria: memoria_sesion");
        var wrote = false;

        wrote |= AppendValue(builder, root, "tema_actual", "Tema actual");
        wrote |= AppendValue(builder, root, "objetivo_actual", "Objetivo actual");
        wrote |= AppendValue(builder, root, "estado", "Estado");
        wrote |= AppendValue(builder, root, "nivel_actual", "Nivel actual");
        wrote |= AppendValue(builder, root, "fecha_ultima_sesion", "Fecha ultima sesion");
        wrote |= AppendValue(builder, root, "ultimo_paso", "Ultimo paso");
        wrote |= AppendValue(builder, root, "ultimo_ejercicio", "Ultimo ejercicio");
        wrote |= AppendValue(builder, root, "siguiente_paso", "Siguiente paso");
        wrote |= AppendValue(builder, root, "proximo_paso", "Proximo paso");
        wrote |= AppendValue(builder, root, "temas_dominados_ultima_sesion", "Temas dominados ultima sesion");
        wrote |= AppendValue(builder, root, "tiempo_invertido_minutos", "Tiempo invertido minutos");
        wrote |= AppendValue(builder, root, "notas", "Notas");

        if (!wrote)
        {
            builder.AppendLine("Sin estado de sesion registrado.");
        }

        return builder.ToString();
    }

    private static string RenderDomainMap(JsonElement root)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Memoria: mapa_dominio");

        if (!TryGetArray(root, "temas", out var topics) && !TryGetArray(root, "habilidades", out topics))
        {
            builder.AppendLine("Sin temas registrados.");
            return builder.ToString();
        }

        var wrote = false;
        foreach (var topic in topics.EnumerateArray())
        {
            wrote = true;
            var name = GetString(topic, "tema") ?? GetString(topic, "habilidad") ?? GetString(topic, "nombre") ?? "Tema sin nombre";
            var level = GetString(topic, "nivel") ?? GetString(topic, "dominio") ?? "sin nivel";
            builder.AppendLine($"- **{name}**: {level}");
            AppendNestedValue(builder, topic, "evidencia", "Evidencia");
            AppendNestedValue(builder, topic, "notas", "Notas");
            AppendNestedValue(builder, topic, "ultima_revision", "Ultima revision");
        }

        if (!wrote)
        {
            builder.AppendLine("Sin temas registrados.");
        }

        return builder.ToString();
    }

    private static string RenderGapsOrErrors(JsonElement root, bool includeResolved)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Memoria: lagunas_o_errores");
        builder.AppendLine("### Activas");
        AppendGapList(builder, root, "activas", emptyMessage: "Sin lagunas activas.");

        if (includeResolved)
        {
            builder.AppendLine();
            builder.AppendLine("### Resueltas");
            AppendGapList(builder, root, "resueltas", emptyMessage: "Sin lagunas resueltas.");
        }

        return builder.ToString();
    }

    private static string RenderActivityHistory(JsonElement root)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Memoria: historial_actividades");

        if (!TryGetArray(root, "actividades", out var activities) && root.ValueKind == JsonValueKind.Array)
        {
            activities = root;
        }

        if (activities.ValueKind != JsonValueKind.Array)
        {
            builder.AppendLine("Sin actividades registradas.");
            return builder.ToString();
        }

        var wrote = false;
        foreach (var activity in activities.EnumerateArray())
        {
            wrote = true;
            var title = GetString(activity, "titulo") ?? GetString(activity, "actividad") ?? GetString(activity, "nombre") ?? "Actividad sin titulo";
            builder.AppendLine($"- **{title}**");
            AppendNestedValue(builder, activity, "tipo", "Tipo");
            AppendNestedValue(builder, activity, "resultado", "Resultado");
            AppendNestedValue(builder, activity, "fecha", "Fecha");
            AppendNestedValue(builder, activity, "notas", "Notas");
        }

        if (!wrote)
        {
            builder.AppendLine("Sin actividades registradas.");
        }

        return builder.ToString();
    }

    private static void AppendGapList(StringBuilder builder, JsonElement root, string propertyName, string emptyMessage)
    {
        if (!TryGetArray(root, propertyName, out var gaps))
        {
            builder.AppendLine(emptyMessage);
            return;
        }

        var wrote = false;
        foreach (var gap in gaps.EnumerateArray())
        {
            wrote = true;
            var topic = GetString(gap, "tema") ?? GetString(gap, "concepto") ?? GetString(gap, "titulo") ?? "Laguna sin titulo";
            builder.AppendLine($"- **{topic}**");
            AppendNestedValue(builder, gap, "descripcion", "Descripcion");
            AppendNestedValue(builder, gap, "evidencia", "Evidencia");
            AppendNestedValue(builder, gap, "plan", "Plan");
            AppendNestedValue(builder, gap, "fecha", "Fecha");
        }

        if (!wrote)
        {
            builder.AppendLine(emptyMessage);
        }
    }

    private static bool AppendValue(StringBuilder builder, JsonElement root, string propertyName, string label)
    {
        if (!root.TryGetProperty(propertyName, out var value) || IsEmpty(value))
        {
            return false;
        }

        builder.AppendLine($"- **{label}:** {ToMarkdownValue(value)}");
        return true;
    }

    private static void AppendNestedValue(StringBuilder builder, JsonElement root, string propertyName, string label)
    {
        if (root.TryGetProperty(propertyName, out var value) && !IsEmpty(value))
        {
            builder.AppendLine($"  - {label}: {ToMarkdownValue(value)}");
        }
    }

    private static bool TryGetArray(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(propertyName, out value)
            && value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(propertyName, out var value) || IsEmpty(value))
        {
            return null;
        }

        return ToMarkdownValue(value);
    }

    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => true,
        JsonValueKind.Undefined => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        _ => false
    };

    private static string ToMarkdownValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Where(item => !IsEmpty(item)).Select(ToMarkdownValue)),
        JsonValueKind.Object => string.Join(", ", value.EnumerateObject()
            .Where(property => !IsEmpty(property.Value))
            .Select(property => $"{property.Name}: {ToMarkdownValue(property.Value)}")),
        _ => string.Empty
    };
}
