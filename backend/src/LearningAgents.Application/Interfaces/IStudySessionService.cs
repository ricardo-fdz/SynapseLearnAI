using LearningAgents.Application.Common;
using LearningAgents.Application.Dtos.StudySessions;

namespace LearningAgents.Application.Interfaces;

public interface IStudySessionService
{
    Task<IReadOnlyList<StudySessionResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<StudySessionResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ServiceResult<StudySessionResponse>> CreateAsync(CreateStudySessionRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> UpdateAsync(int id, UpdateStudySessionRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
