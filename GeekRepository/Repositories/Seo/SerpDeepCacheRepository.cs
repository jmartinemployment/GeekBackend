using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class SerpDeepCacheRepository(SeoDbContext db) : ISerpDeepCacheRepository
{
    public async Task<Result<SeoSerpDeepCache?>> GetAsync(
        string keyword,
        string location,
        int resultCount,
        CancellationToken ct = default)
    {
        var normalizedKeyword = keyword.Trim().ToLowerInvariant();
        var normalizedLocation = location.Trim();
        var row = await db.SerpDeepCache.AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Keyword == normalizedKeyword
                    && c.Location == normalizedLocation
                    && c.ResultCount == resultCount,
                ct);
        return Result<SeoSerpDeepCache?>.Success(row);
    }

    public async Task<Result<SeoSerpDeepCache>> UpsertAsync(SeoSerpDeepCache entry, CancellationToken ct = default)
    {
        entry.Keyword = entry.Keyword.Trim().ToLowerInvariant();
        entry.Location = entry.Location.Trim();

        var existing = await db.SerpDeepCache.FirstOrDefaultAsync(
            c => c.Keyword == entry.Keyword
                && c.Location == entry.Location
                && c.ResultCount == entry.ResultCount,
            ct);

        if (existing is null)
        {
            entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
            db.SerpDeepCache.Add(entry);
        }
        else
        {
            existing.ResultsJson = entry.ResultsJson;
            existing.TermMatrixJson = entry.TermMatrixJson;
            existing.FetchedAt = entry.FetchedAt;
            existing.ExpiresAt = entry.ExpiresAt;
            entry = existing;
        }

        await db.SaveChangesAsync(ct);
        return Result<SeoSerpDeepCache>.Success(entry);
    }
}
