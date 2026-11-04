using System.Text.Json;
using System.Text.Json.Nodes;
using LearningAgents.Application.Dtos.Tutors;
using LearningAgents.Application.Interfaces;
using LearningAgents.Domain.Entities;
using LearningAgents.Domain.Memory;
using Microsoft.EntityFrameworkCore;

namespace LearningAgents.Application.Services;

internal sealed class TutorService(
    ILearningAgentsDbContext dbContext,
    ILlmProfileCatalog llmProfileCatalog) : ITutorService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

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

        await using var transaction = await dbContext.BeginTransactionAsync(cancellationToken);
        var tutor = new Tutor
        {
            Name = request.Name,
            Description = request.Description,
            SystemPromptContent = request.SystemPromptContent,
            GeminiModel = ResolveGeminiModel(request.GeminiModel),
            LlmProfile = ResolveLlmProfile(request.LlmProfile),
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
        if (!string.IsNullOrWhiteSpace(request.GeminiModel))
        {
            tutor.GeminiModel = request.GeminiModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.LlmProfile))
        {
            tutor.LlmProfile = request.LlmProfile.Trim();
        }

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
        string.IsNullOrWhiteSpace(requestedModel) ? llmProfileCatalog.DefaultModel : requestedModel.Trim();

    private string ResolveLlmProfile(string? requestedProfile) =>
        string.IsNullOrWhiteSpace(requestedProfile) ? llmProfileCatalog.DefaultProfile : requestedProfile.Trim();

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
