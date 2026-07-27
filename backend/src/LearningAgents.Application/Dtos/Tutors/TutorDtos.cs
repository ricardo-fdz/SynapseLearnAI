using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LearningAgents.Application.Dtos.Tutors;

public sealed record TutorResponse(
    int Id,
    string Name,
    string Description,
    string SystemPromptContent,
    string GeminiModel,
    string LlmProfile,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateTutorRequest(
    [param: Required, StringLength(200)] string Name,
    [param: Required, StringLength(1000)] string Description,
    [param: Required] string SystemPromptContent,
    [param: StringLength(100)] string? GeminiModel = null,
    InitialStudentProfileRequest? InitialStudentProfile = null);

public sealed record InitialStudentProfileRequest(
    [property: JsonPropertyName("alias")] string? Alias = null,
    [property: JsonPropertyName("lenguaje_principal")] string? LenguajePrincipal = null,
    [property: JsonPropertyName("objetivo_declarado")] string? ObjetivoDeclarado = null,
    [property: JsonPropertyName("estilo_aprendizaje")] InitialLearningStyleRequest? EstiloAprendizaje = null,
    [property: JsonPropertyName("preferencias_comunicacion")] InitialCommunicationPreferencesRequest? PreferenciasComunicacion = null);

public sealed record InitialLearningStyleRequest(
    [property: JsonPropertyName("prefiere")] string? Prefiere = null,
    [property: JsonPropertyName("ritmo_sesion")] string? RitmoSesion = null,
    [property: JsonPropertyName("reaccion_ante_errores")] string? ReaccionAnteErrores = null,
    [property: JsonPropertyName("nivel_autonomia")] string? NivelAutonomia = null);

public sealed record InitialCommunicationPreferencesRequest(
    [property: JsonPropertyName("idioma")] string? Idioma = null,
    [property: JsonPropertyName("tono_tutor")] string? TonoTutor = null);

public sealed record UpdateTutorRequest(
    [param: Required, StringLength(200)] string Name,
    [param: Required, StringLength(1000)] string Description,
    [param: Required] string SystemPromptContent,
    [param: Required, StringLength(100)] string GeminiModel);
