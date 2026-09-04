using System.Text.Json;
using System.Text.Json.Nodes;
using LearningAgents.Application.Dtos.Tutors;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Memory;
using LearningAgents.Infrastructure.LLM;
using LearningAgents.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LearningAgents.Application.Services;

internal sealed class TutorService(
    LearningAgentsDbContext dbContext,
    IOptions<GeminiOptions> geminiOptions,
    IOptions<LlmProfilesOptions> llmProfilesOptions) : ITutorService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly GeminiOptions geminiOptions = geminiOptions.Value;
    private readonly LlmProfilesOptions llmProfilesOptions = llmProfilesOptions.Value;

    public async Task<IReadOnlyList<TutorResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Tutors
            .AsNoTracking()
            .OrderBy(tutor => tutor.Id)
            .Select(tutor => ToResponse(tutor))
            .ToListAsync(cancellationToken);
    }

    public async Task<TutorResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var tutor = await dbContext.Tutors
            .AsNoTracking()
            .FirstOrDefaultAsync(tutor => tutor.Id == id, cancellationToken);

        return tutor is null ? null : ToResponse(tutor);
    }

    public async Task<TutorResponse> CreateAsync(CreateTutorRequest request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var tutor = new Tutor
        {
            Name = request.Name,
            Description = request.Description,
            SystemPromptContent = request.SystemPromptContent,
            GeminiModel = ResolveGeminiModel(request.GeminiModel),
            LlmProfile = ResolveDefaultLlmProfile(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Tutors.Add(tutor);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.MemoryEntries.AddRange(MemoryEntryDefaults.CreateForTutor(
            tutor.Id,
            now,
            BuildInitialStudentProfileJson(request.InitialStudentProfile),
            MemoryEntryDefaults.DetectDomainMapJson(request.SystemPromptContent)));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(tutor);
    }

    public async Task<bool> UpdateAsync(int id, UpdateTutorRequest request, CancellationToken cancellationToken)
    {
        var tutor = await dbContext.Tutors.FirstOrDefaultAsync(tutor => tutor.Id == id, cancellationToken);
        if (tutor is null)
        {
            return false;
        }

        tutor.Name = request.Name;
        tutor.Description = request.Description;
        tutor.SystemPromptContent = request.SystemPromptContent;
        tutor.GeminiModel = request.GeminiModel;
        tutor.LlmProfile = ResolveDefaultLlmProfile();
        tutor.UpdatedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var tutor = await dbContext.Tutors.FirstOrDefaultAsync(tutor => tutor.Id == id, cancellationToken);
        if (tutor is null)
        {
            return false;
        }

        dbContext.Tutors.Remove(tutor);
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static TutorResponse ToResponse(Tutor tutor) => new(
        tutor.Id,
        tutor.Name,
        tutor.Description,
        tutor.SystemPromptContent,
        tutor.GeminiModel,
        tutor.LlmProfile,
        tutor.CreatedAtUtc,
        tutor.UpdatedAtUtc);

    private string ResolveGeminiModel(string? requestedModel) =>
        string.IsNullOrWhiteSpace(requestedModel) ? geminiOptions.DefaultModel : requestedModel;

    private string ResolveDefaultLlmProfile() =>
        string.IsNullOrWhiteSpace(llmProfilesOptions.DefaultProfile) ? "gemini-default" : llmProfilesOptions.DefaultProfile;

    private static string BuildInitialStudentProfileJson(InitialStudentProfileRequest? profile)
    {
        if (profile is null)
        {
            return MemoryEntryDefaults.StudentProfileJson;
        }

        var root = new JsonObject();
        AddString(root, "alias", profile.Alias);
        AddString(root, "lenguaje_principal", profile.LenguajePrincipal);
        AddString(root, "objetivo_declarado", profile.ObjetivoDeclarado);

        if (profile.EstiloAprendizaje is not null)
        {
            var style = new JsonObject();
            AddString(style, "prefiere", profile.EstiloAprendizaje.Prefiere);
            AddString(style, "ritmo_sesion", profile.EstiloAprendizaje.RitmoSesion);
            AddString(style, "reaccion_ante_errores", profile.EstiloAprendizaje.ReaccionAnteErrores);
            AddString(style, "nivel_autonomia", profile.EstiloAprendizaje.NivelAutonomia);
            if (style.Count > 0)
            {
                root["estilo_aprendizaje"] = style;
            }
        }

        if (profile.PreferenciasComunicacion is not null)
        {
            var preferences = new JsonObject();
            AddString(preferences, "idioma", profile.PreferenciasComunicacion.Idioma);
            AddString(preferences, "tono_tutor", profile.PreferenciasComunicacion.TonoTutor);
            if (preferences.Count > 0)
            {
                root["preferencias_comunicacion"] = preferences;
            }
        }

        return root.Count == 0 ? MemoryEntryDefaults.StudentProfileJson : root.ToJsonString(JsonOptions);
    }

    private static void AddString(JsonObject target, string propertyName, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[propertyName] = value;
        }
    }
}
