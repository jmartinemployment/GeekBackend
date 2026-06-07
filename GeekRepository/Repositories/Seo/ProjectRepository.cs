using GeekSeo.Persistence.Entities;
using GeekSeo.Application.Infrastructure;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
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
        foreach (var project in list)
            await TryPersistUrlNormalizationAsync(project, ct);
        return Result<IReadOnlyList<SeoProject>>.Success(list);
    }

    public async Task<Result<SeoProject>> GetByIdAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            return Result<SeoProject>.NotFound("Project not found");
        await TryPersistUrlNormalizationAsync(project, ct);
        return Result<SeoProject>.Success(project);
    }

    public async Task<Result<SeoProject>> GetByIdAsync(Guid projectId, Guid userId, CancellationToken ct = default)
    {
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);
        if (project is null)
            return Result<SeoProject>.NotFound("Project not found");
        await TryPersistUrlNormalizationAsync(project, ct);
        return Result<SeoProject>.Success(project);
    }

    public async Task<Result<SeoProject>> CreateAsync(Guid userId, CreateProjectRequest request, CancellationToken ct = default)
    {
        if (!SeoSiteUrlNormalizer.TryNormalize(request.Url, out var siteUrl, out var urlError))
            return Result<SeoProject>.Failure(urlError);

        var now = DateTimeOffset.UtcNow;
        var project = new SeoProject
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name,
            Url = siteUrl,
            DefaultLocation = request.DefaultLocation,
            DefaultLanguage = request.DefaultLanguage,
            BusinessAddress = NormalizeAddress(request.BusinessAddress),
            ServiceRadiusMiles = LocalServiceAreaDefaults.ClampRadiusMiles(request.ServiceRadiusMiles),
            LocalSeoEnabled = request.LocalSeoEnabled,
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
        if (request.Url is not null)
        {
            if (!SeoSiteUrlNormalizer.TryNormalize(request.Url, out var siteUrl, out var urlError))
                return Result<SeoProject>.Failure(urlError);
            project.Url = siteUrl;
        }
        if (request.DefaultLocation is not null) project.DefaultLocation = request.DefaultLocation;
        if (request.DefaultLanguage is not null) project.DefaultLanguage = request.DefaultLanguage;
        if (request.BusinessAddress is not null)
            project.BusinessAddress = NormalizeAddress(request.BusinessAddress);
        if (request.ServiceRadiusMiles is int radius)
            project.ServiceRadiusMiles = LocalServiceAreaDefaults.ClampRadiusMiles(radius);
        if (request.LocalSeoEnabled is bool enabled)
            project.LocalSeoEnabled = enabled;
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

    private async Task TryPersistUrlNormalizationAsync(SeoProject project, CancellationToken ct)
    {
        if (!SeoSiteUrlNormalizer.TryNormalize(project.Url, out var normalized, out _))
            return;
        if (string.Equals(project.Url, normalized, StringComparison.Ordinal))
            return;

        var tracked = await db.Projects.FirstOrDefaultAsync(p => p.Id == project.Id, ct);
        if (tracked is null)
            return;

        tracked.Url = normalized;
        tracked.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        project.Url = normalized;
    }

    private static string? NormalizeAddress(string? address)
    {
        if (address is null)
            return null;
        var trimmed = address.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
