using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class TopicalMapRepository(SeoDbContext db) : ITopicalMapRepository
{
    public async Task<Result<SeoTopicalMap?>> GetByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var map = await db.TopicalMaps.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId, ct);
        return Result<SeoTopicalMap?>.Success(map);
    }

    public async Task<Result<SeoTopicalMap>> UpsertAsync(SeoTopicalMap map, CancellationToken ct = default)
    {
        var existing = await db.TopicalMaps.FirstOrDefaultAsync(m => m.ProjectId == map.ProjectId, ct);
        if (existing is null)
        {
            map.Id = map.Id == Guid.Empty ? Guid.NewGuid() : map.Id;
            db.TopicalMaps.Add(map);
        }
        else
        {
            existing.Status = map.Status;
            existing.ClustersJson = map.ClustersJson;
            existing.ContentGapsJson = map.ContentGapsJson;
            existing.GeneratedAt = map.GeneratedAt;
            existing.ExpiresAt = map.ExpiresAt;
            map = existing;
        }

        await db.SaveChangesAsync(ct);
        return Result<SeoTopicalMap>.Success(map);
    }

    public async Task<Result<IReadOnlyList<TopicalMapRefreshCandidate>>> ListDueForRefreshAsync(
        int limit,
        CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(1);
        var list = await (
            from map in db.TopicalMaps.AsNoTracking()
            join project in db.Projects.AsNoTracking() on map.ProjectId equals project.Id
            where map.ExpiresAt != null && map.ExpiresAt <= cutoff
            orderby map.ExpiresAt
            select new TopicalMapRefreshCandidate
            {
                ProjectId = map.ProjectId,
                UserId = project.UserId,
                TopicalMapId = map.Id,
            })
            .Take(limit)
            .ToListAsync(ct);

        return Result<IReadOnlyList<TopicalMapRefreshCandidate>>.Success(list);
    }
}
