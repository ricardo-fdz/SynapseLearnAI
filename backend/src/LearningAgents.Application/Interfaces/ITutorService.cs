using LearningAgents.Application.Dtos.Tutors;

namespace LearningAgents.Application.Interfaces;

public interface ITutorService
{
    Task<IReadOnlyList<TutorResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TutorResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<TutorResponse> CreateAsync(CreateTutorRequest request, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(int id, UpdateTutorRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
