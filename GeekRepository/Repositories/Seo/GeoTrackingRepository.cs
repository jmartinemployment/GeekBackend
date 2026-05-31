using System.Text.Json;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class GeoTrackingRepository(SeoDbContext db) : IGeoTrackingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<Result<IReadOnlyList<SeoGeoTrackingQuery>>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var list = await db.GeoTrackingQueries.AsNoTracking()
            .Where(q => q.ProjectId == projectId)
            .OrderByDescending(q => q.QueryText)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoGeoTrackingQuery>>.Success(list);
    }

    public async Task<Result<SeoGeoTrackingQuery>> CreateAsync(SeoGeoTrackingQuery query, CancellationToken ct = default)
    {
        query.Id = query.Id == Guid.Empty ? Guid.NewGuid() : query.Id;
        db.GeoTrackingQueries.Add(query);
        await db.SaveChangesAsync(ct);
        return Result<SeoGeoTrackingQuery>.Success(query);
    }

    public async Task<Result> DeleteAsync(Guid queryId, CancellationToken ct = default)
    {
        var query = await db.GeoTrackingQueries.FirstOrDefaultAsync(q => q.Id == queryId, ct);
        if (query is null)
            return Result.Failure("Query not found");

        var snapshots = await db.GeoMentionSnapshots.Where(s => s.QueryId == queryId).ToListAsync(ct);
        db.GeoMentionSnapshots.RemoveRange(snapshots);
        db.GeoTrackingQueries.Remove(query);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<SeoGeoTrackingQuery?>> GetQueryAsync(Guid queryId, CancellationToken ct = default)
    {
        var query = await db.GeoTrackingQueries.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == queryId, ct);
        return Result<SeoGeoTrackingQuery?>.Success(query);
    }

    public async Task<Result<IReadOnlyList<SeoGeoMentionSnapshot>>> ListSnapshotsAsync(
        Guid queryId,
        int days,
        CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        var list = await db.GeoMentionSnapshots.AsNoTracking()
            .Where(s => s.QueryId == queryId && s.CheckedAt >= cutoff)
            .OrderBy(s => s.CheckedAt)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoGeoMentionSnapshot>>.Success(list);
    }

    public async Task<Result> AddSnapshotAsync(SeoGeoMentionSnapshot snapshot, CancellationToken ct = default)
    {
        snapshot.Id = snapshot.Id == Guid.Empty ? Guid.NewGuid() : snapshot.Id;
        db.GeoMentionSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<GeoProbeCandidate>>> ListEnabledQueriesAsync(
        int limit,
        CancellationToken ct = default)
    {
        var list = await (
            from query in db.GeoTrackingQueries.AsNoTracking()
            join project in db.Projects.AsNoTracking() on query.ProjectId equals project.Id
            where query.Enabled
            orderby query.QueryText
            select new GeoProbeCandidate
            {
                QueryId = query.Id,
                ProjectId = query.ProjectId,
                UserId = project.UserId,
                QueryText = query.QueryText,
                PlatformsJson = query.PlatformsJson,
            })
            .Take(limit)
            .ToListAsync(ct);

        return Result<IReadOnlyList<GeoProbeCandidate>>.Success(list);
    }
}
