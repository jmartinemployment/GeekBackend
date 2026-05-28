using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class ProjectRepository(SeoDbContext db) : IProjectRepository
{
    public async Task<Result<IReadOnlyList<SeoProject>>> ListByUserAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await db.Projects.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoProject>>.Success(list);
    }

    public async Task<Result<SeoProject>> GetByIdAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        return project is null
            ? Result<SeoProject>.NotFound("Project not found")
            : Result<SeoProject>.Success(project);
    }

    public async Task<Result<SeoProject>> GetByIdAsync(Guid projectId, Guid userId, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);
        return project is null
            ? Result<SeoProject>.NotFound("Project not found")
            : Result<SeoProject>.Success(project);
    }

    public async Task<Result<SeoProject>> CreateAsync(Guid userId, CreateProjectRequest request, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var project = new SeoProject
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name,
            Url = request.Url,
            DefaultLocation = request.DefaultLocation,
            DefaultLanguage = request.DefaultLanguage,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return Result<SeoProject>.Success(project);
    }

    public async Task<Result<SeoProject>> UpdateAsync(Guid projectId, UpdateProjectRequest request, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            return Result<SeoProject>.NotFound("Project not found");

        if (request.Name is not null) project.Name = request.Name;
        if (request.Url is not null) project.Url = request.Url;
        if (request.DefaultLocation is not null) project.DefaultLocation = request.DefaultLocation;
        if (request.DefaultLanguage is not null) project.DefaultLanguage = request.DefaultLanguage;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<SeoProject>.Success(project);
    }

    public async Task<Result> DeleteAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            return Result.Failure("Project not found");
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
