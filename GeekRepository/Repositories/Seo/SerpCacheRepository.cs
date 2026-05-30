using System.Text.Json;
using GeekSeo.Persistence.Entities;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;
using GeekSeo.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class SerpCacheRepository(SeoDbContext db) : ISerpCacheRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<Result<SeoSerpResult?>> GetAsync(
        string keyword, string location, string languageCode, CancellationToken ct = default)
    {
        var row = await db.SerpResults.AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.Keyword == keyword && s.Location == location && s.LanguageCode == languageCode, ct);
        return Result<SeoSerpResult?>.Success(row);
    }

    public async Task<Result<SeoSerpResult>> UpsertAsync(
        string keyword, string location, string languageCode,
        SerpResult serp, SerpBenchmarksPayload benchmarks,
        CancellationToken ct = default)
    {
        var existing = await db.SerpResults.FirstOrDefaultAsync(s =>
            s.Keyword == keyword && s.Location == location && s.LanguageCode == languageCode, ct);

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(24);

        if (existing is null)
        {
            existing = new SeoSerpResult
            {
                Id = Guid.NewGuid(),
                Keyword = keyword,
                Location = location,
                LanguageCode = languageCode,
            };
            db.SerpResults.Add(existing);
        }

        existing.ResultsJson = JsonSerializer.Serialize(benchmarks, JsonOptions);
        existing.PeopleAlsoAskJson = JsonSerializer.Serialize(serp.PeopleAlsoAsk, JsonOptions);
        existing.RelatedSearchesJson = JsonSerializer.Serialize(serp.RelatedSearches, JsonOptions);
        existing.FeaturedSnippet = serp.FeaturedSnippetText;
        existing.SerpFeaturesJson = JsonSerializer.Serialize(serp.Features, JsonOptions);
        existing.FetchedAt = serp.FetchedAt;
        existing.ExpiresAt = expires;

        await db.SaveChangesAsync(ct);
        return Result<SeoSerpResult>.Success(existing);
    }

    public async Task<Result> DeleteAsync(
        string keyword, string location, string languageCode, CancellationToken ct = default)
    {
        var rows = await db.SerpResults
            .Where(s => s.Keyword == keyword && s.Location == location && s.LanguageCode == languageCode)
            .ToListAsync(ct);
        if (rows.Count == 0)
            return Result.Success();

        db.SerpResults.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
