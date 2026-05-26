using GeekApplication.Entities.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Interfaces.Seo;

public interface IProjectService
{
    Task<Result<IReadOnlyList<SeoProject>>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<Result<SeoProject>> GetAsync(Guid userId, Guid projectId, CancellationToken ct = default);
    Task<Result<SeoProject>> CreateAsync(Guid userId, CreateProjectRequest request, CancellationToken ct = default);
    Task<Result<SeoProject>> UpdateAsync(Guid userId, Guid projectId, UpdateProjectRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid userId, Guid projectId, CancellationToken ct = default);
}
