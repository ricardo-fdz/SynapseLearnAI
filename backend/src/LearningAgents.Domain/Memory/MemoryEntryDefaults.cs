using LearningAgents.Domain.Entities;

namespace LearningAgents.Domain.Memory;

public static class MemoryEntryDefaults
{
    public const string SessionMemoryJson = "{}";
    public const string StudentProfileJson = "{}";
    public const string DomainMapJsonTemas = "{\"temas\":[]}";
    public const string DomainMapJsonHabilidades = "{\"habilidades\":[]}";
    public const string GapsOrErrorsJson = "{\"activas\":[],\"resueltas\":[]}";
    public const string ActivityHistoryJson = "{\"proyectos\":[]}";
    public const string RoadmapJson = "{\"roadmaps\":[]}";

    public static IReadOnlyList<MemoryEntry> CreateForTutor(
        int tutorId,
        DateTime now,
        string? studentProfileJson = null,
        string? domainMapJson = null) =>
    [
        Create(tutorId, MemoryKeys.SessionMemory, SessionMemoryJson, now),
        Create(tutorId, MemoryKeys.StudentProfile, string.IsNullOrWhiteSpace(studentProfileJson) ? StudentProfileJson : studentProfileJson, now),
        Create(tutorId, MemoryKeys.DomainMap, domainMapJson ?? DomainMapJsonTemas, now),
        Create(tutorId, MemoryKeys.GapsOrErrors, GapsOrErrorsJson, now),
        Create(tutorId, MemoryKeys.ActivityHistory, ActivityHistoryJson, now),
        Create(tutorId, MemoryKeys.Roadmap, RoadmapJson, now)
    ];

    public static string DetectDomainMapJson(string? systemPromptContent)
    {
        if (string.IsNullOrWhiteSpace(systemPromptContent))
            return DomainMapJsonTemas;

        var lower = systemPromptContent.ToLowerInvariant();
        var langKeywords = new[] { "idioma", "language", "inglés", "english", "español", "spanish", "francés", "french", "alemán", "german", "mcér", "mcer", "cefr" };

        return langKeywords.Any(k => lower.Contains(k))
            ? DomainMapJsonHabilidades
            : DomainMapJsonTemas;
    }

    private static MemoryEntry Create(int tutorId, string key, string valueJson, DateTime now) => new()
    {
        TutorId = tutorId,
        Key = key,
        ValueJson = valueJson,
        SchemaVersion = 1,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };
}
