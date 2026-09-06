namespace LearningAgents.Domain.Memory;

public static class MemoryKeys
{
    public const string SessionMemory = "memoria_sesion";
    public const string StudentProfile = "perfil_estudiante";
    public const string DomainMap = "mapa_dominio";
    public const string GapsOrErrors = "lagunas_o_errores";
    public const string ActivityHistory = "historial_actividades";
    public const string Roadmap = "roadmap";

    public static readonly string[] All =
    [
        SessionMemory,
        StudentProfile,
        DomainMap,
        GapsOrErrors,
        ActivityHistory,
        Roadmap
    ];

    public static bool IsStandard(string key) => All.Contains(key);
}
