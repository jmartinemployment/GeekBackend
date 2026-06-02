using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class RankTrackingRepository(SeoDbContext db) : IRankTrackingRepository
{
    public async Task<Result<IReadOnlyList<SeoTrackedKeyword>>> GetKeywordsAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var list = await db.TrackedKeywords.AsNoTracking()
            .Where(k => k.ProjectId == projectId)
            .OrderBy(k => k.Keyword)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoTrackedKeyword>>.Success(list);
    }

    public async Task<Result<SeoTrackedKeyword>> AddKeywordAsync(
        SeoTrackedKeyword entity,
        CancellationToken ct = default)
    {
        entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
        if (entity.AddedAt == default)
            entity.AddedAt = DateTimeOffset.UtcNow;

        var exists = await db.TrackedKeywords.AnyAsync(
            k => k.ProjectId == entity.ProjectId && k.Keyword == entity.Keyword,
            ct);
        if (exists)
            return Result<SeoTrackedKeyword>.Failure("Keyword is already tracked for this project");

        db.TrackedKeywords.Add(entity);
        await db.SaveChangesAsync(ct);
        return Result<SeoTrackedKeyword>.Success(entity);
    }

    public async Task<Result> DeleteKeywordAsync(Guid keywordId, CancellationToken ct = default)
    {
        var keyword = await db.TrackedKeywords.FirstOrDefaultAsync(k => k.Id == keywordId, ct);
        if (keyword is null)
            return Result.Failure("Tracked keyword not found");

        var snapshots = await db.RankTracking
            .Where(s => s.ProjectId == keyword.ProjectId && s.Keyword == keyword.Keyword)
            .ToListAsync(ct);
        if (snapshots.Count > 0)
            db.RankTracking.RemoveRange(snapshots);

        db.TrackedKeywords.Remove(keyword);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SeoRankTracking>>> GetHistoryAsync(
        Guid projectId,
        string keyword,
        int days,
        CancellationToken ct = default)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 366)));
        var list = await db.RankTracking.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.Keyword == keyword && s.Date >= cutoff)
            .OrderBy(s => s.Date)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoRankTracking>>.Success(list);
    }

    public async Task<Result> UpsertSnapshotAsync(SeoRankTracking snapshot, CancellationToken ct = default)
    {
        snapshot.Id = snapshot.Id == Guid.Empty ? Guid.NewGuid() : snapshot.Id;

        var existing = await db.RankTracking.FirstOrDefaultAsync(
            s => s.ProjectId == snapshot.ProjectId
                && s.Keyword == snapshot.Keyword
                && s.Date == snapshot.Date,
            ct);

        if (existing is null)
        {
            db.RankTracking.Add(snapshot);
        }
        else
        {
            existing.Position = snapshot.Position;
            existing.PageUrl = snapshot.PageUrl;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<Guid>>> ListProjectsWithKeywordsAsync(
        int limit,
        CancellationToken ct = default)
    {
        var list = await db.TrackedKeywords.AsNoTracking()
            .Where(k => k.Enabled)
            .Select(k => k.ProjectId)
            .Distinct()
            .OrderBy(id => id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
        return Result<IReadOnlyList<Guid>>.Success(list);
    }
}
