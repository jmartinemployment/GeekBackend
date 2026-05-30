using GeekSeo.Persistence.Entities;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;
using GeekSeo.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class KeywordRepository(SeoDbContext db) : IKeywordRepository
{
    public async Task<Result<IReadOnlyList<SeoKeyword>>> GetByProjectAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var rows = await db.Keywords.AsNoTracking()
            .Where(k => k.ProjectId == projectId)
            .OrderByDescending(k => k.SearchVolume)
            .ToListAsync(ct);
        return Result<IReadOnlyList<SeoKeyword>>.Success(rows);
    }

    public async Task<Result> BulkUpsertAsync(
        Guid projectId, IReadOnlyList<KeywordResult> keywords, string location, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kw in keywords)
        {
            var existing = await db.Keywords.FirstOrDefaultAsync(
                k => k.ProjectId == projectId && k.Keyword == kw.Keyword && k.Location == location, ct);
            if (existing is null)
            {
                db.Keywords.Add(new SeoKeyword
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    Keyword = kw.Keyword,
                    Location = location,
                    SearchVolume = kw.SearchVolume,
                    KeywordDifficulty = (decimal)kw.KeywordDifficulty,
                    CachedAt = now,
                });
            }
            else
            {
                existing.SearchVolume = kw.SearchVolume;
                existing.KeywordDifficulty = (decimal)kw.KeywordDifficulty;
                existing.CachedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
