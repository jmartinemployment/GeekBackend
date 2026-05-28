using GeekApplication.Entities.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IProjectRepository
{
    Task<Result<IReadOnlyList<SeoProject>>> ListByUserAsync(Guid userId, CancellationToken ct = default);
    Task<Result<SeoProject>> GetByIdAsync(Guid projectId, CancellationToken ct = default);
    Task<Result<SeoProject>> GetByIdAsync(Guid projectId, Guid userId, CancellationToken ct = default);
    Task<Result<SeoProject>> CreateAsync(Guid userId, CreateProjectRequest request, CancellationToken ct = default);
    Task<Result<SeoProject>> UpdateAsync(Guid projectId, UpdateProjectRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid projectId, CancellationToken ct = default);
}
