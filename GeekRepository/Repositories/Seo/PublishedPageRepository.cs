using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class PublishedPageRepository(SeoDbContext db) : IPublishedPageRepository
{
    public async Task<Result<IReadOnlyList<SeoPublishedPage>>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var list = await db.PublishedPages.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.LastAuditAt)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoPublishedPage>>.Success(list);
    }

    public async Task<Result<SeoPublishedPage?>> GetByIdAsync(Guid publishedPageId, CancellationToken ct = default)
    {
        var page = await db.PublishedPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == publishedPageId, ct);
        return Result<SeoPublishedPage?>.Success(page);
    }

    public async Task<Result<IReadOnlyList<PerformanceSnapshotPoint>>> GetSparklineAsync(
        Guid publishedPageId,
        int days,
        CancellationToken ct = default)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days);
        var rows = await db.ContentPerformanceSnapshots.AsNoTracking()
            .Where(s => s.PublishedPageId == publishedPageId && s.Date >= start)
            .OrderBy(s => s.Date)
            .Select(s => new PerformanceSnapshotPoint
            {
                Date = s.Date.ToString("yyyy-MM-dd"),
                Clicks = s.Clicks,
                Impressions = s.Impressions,
                Position = s.Position,
            })
            .ToListAsync(ct);

        return Result<IReadOnlyList<PerformanceSnapshotPoint>>.Success(rows);
    }

    public async Task<Result> UpsertSnapshotAsync(SeoContentPerformanceSnapshot snapshot, CancellationToken ct = default)
    {
        var existing = await db.ContentPerformanceSnapshots
            .FirstOrDefaultAsync(
                s => s.PublishedPageId == snapshot.PublishedPageId && s.Date == snapshot.Date,
                ct);

        if (existing is null)
        {
            snapshot.Id = snapshot.Id == Guid.Empty ? Guid.NewGuid() : snapshot.Id;
            db.ContentPerformanceSnapshots.Add(snapshot);
        }
        else
        {
            existing.Position = snapshot.Position;
            existing.Impressions = snapshot.Impressions;
            existing.Clicks = snapshot.Clicks;
            existing.Ctr = snapshot.Ctr;
        }

        var page = await db.PublishedPages.FirstOrDefaultAsync(p => p.Id == snapshot.PublishedPageId, ct);
        if (page is not null)
            page.LastAuditAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PublishedPageRefreshCandidate>>> ListDueForSnapshotAsync(
        int limit,
        CancellationToken ct = default)
    {
        var staleBefore = DateTimeOffset.UtcNow.AddDays(-6);
        var list = await (
            from page in db.PublishedPages.AsNoTracking()
            join project in db.Projects.AsNoTracking() on page.ProjectId equals project.Id
            where page.LastAuditAt == null || page.LastAuditAt <= staleBefore
            orderby page.LastAuditAt
            select new PublishedPageRefreshCandidate
            {
                PublishedPageId = page.Id,
                ProjectId = page.ProjectId,
                UserId = project.UserId,
                Url = page.Url,
            })
            .Take(limit)
            .ToListAsync(ct);

        return Result<IReadOnlyList<PublishedPageRefreshCandidate>>.Success(list);
    }
}
