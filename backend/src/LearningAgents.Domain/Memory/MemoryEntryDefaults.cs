using LearningAgents.Domain.Entities;

namespace LearningAgents.Domain.Memory;

public static class MemoryEntryDefaults
{
    public const string SessionMemoryJson = "{}";
    public const string StudentProfileJson = "{}";
    public const string DomainMapJson = "{\"temas\":[]}";
    public const string GapsOrErrorsJson = "{\"activas\":[],\"resueltas\":[]}";
    public const string ActivityHistoryJson = "{\"proyectos\":[]}";

    public static IReadOnlyList<MemoryEntry> CreateForTutor(int tutorId, DateTime now, string? studentProfileJson = null) =>
    [
        Create(tutorId, MemoryKeys.SessionMemory, SessionMemoryJson, now),
        Create(tutorId, MemoryKeys.StudentProfile, string.IsNullOrWhiteSpace(studentProfileJson) ? StudentProfileJson : studentProfileJson, now),
        Create(tutorId, MemoryKeys.DomainMap, DomainMapJson, now),
        Create(tutorId, MemoryKeys.GapsOrErrors, GapsOrErrorsJson, now),
        Create(tutorId, MemoryKeys.ActivityHistory, ActivityHistoryJson, now)
    ];

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
