using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class ProjectService(IProjectRepository projects) : IProjectService
{
    public async Task<Result<IReadOnlyList<SeoProject>>> ListAsync(Guid userId, CancellationToken ct = default) =>
        await projects.ListByUserAsync(userId, ct);

    public async Task<Result<SeoProject>> GetAsync(Guid userId, Guid projectId, CancellationToken ct = default)
    {
        var result = await projects.GetByIdAsync(projectId, ct);
        if (!result.IsSuccess || result.Value is null)
            return Result<SeoProject>.NotFound("Project not found");
        if (result.Value.UserId != userId)
            return Result<SeoProject>.Failure("Access denied");
        return result;
    }

    public Task<Result<SeoProject>> CreateAsync(Guid userId, CreateProjectRequest request, CancellationToken ct = default) =>
        projects.CreateAsync(userId, request, ct);

    public async Task<Result<SeoProject>> UpdateAsync(
        Guid userId, Guid projectId, UpdateProjectRequest request, CancellationToken ct = default)
    {
        var access = await GetAsync(userId, projectId, ct);
        if (!access.IsSuccess)
            return access;
        return await projects.UpdateAsync(projectId, request, ct);
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid projectId, CancellationToken ct = default)
    {
        var access = await GetAsync(userId, projectId, ct);
        if (!access.IsSuccess)
            return Result.Failure(access.Error ?? "Access denied");
        return await projects.DeleteAsync(projectId, ct);
    }
}
