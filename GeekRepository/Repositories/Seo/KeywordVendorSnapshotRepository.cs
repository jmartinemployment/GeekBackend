using GeekSeo.Application.Configuration;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Results;
using GeekSeo.Persistence.Data;
using GeekSeo.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories.Seo;

public sealed class KeywordVendorSnapshotRepository(SeoDbContext db) : IKeywordVendorSnapshotRepository
{
    public async Task<Result<SeoKeywordVendorSnapshot?>> GetAsync(
        string seedKeyword,
        string location,
        string languageCode,
        CancellationToken ct = default)
    {
        var seed = NormalizeSeed(seedKeyword);
        var loc = location.Trim();
        var lang = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim();

        var row = await db.KeywordVendorSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.SeedKeyword == seed && s.Location == loc && s.LanguageCode == lang,
                ct);

        return Result<SeoKeywordVendorSnapshot?>.Success(row);
    }

    public async Task<Result<SeoKeywordVendorSnapshot>> UpsertAsync(
        SeoKeywordVendorSnapshot entry,
        CancellationToken ct = default)
    {
        entry.SeedKeyword = NormalizeSeed(entry.SeedKeyword);
        entry.Location = entry.Location.Trim();
        entry.LanguageCode = string.IsNullOrWhiteSpace(entry.LanguageCode) ? "en" : entry.LanguageCode.Trim();

        if (entry.ExpiresAt == default)
            entry.ExpiresAt = entry.FetchedAt.AddDays(VendorPersistenceSettings.KeywordRetentionDays);

        var existing = await db.KeywordVendorSnapshots.FirstOrDefaultAsync(
            s => s.SeedKeyword == entry.SeedKeyword
                && s.Location == entry.Location
                && s.LanguageCode == entry.LanguageCode,
            ct);

        if (existing is null)
        {
            entry.Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id;
            db.KeywordVendorSnapshots.Add(entry);
        }
        else
        {
            existing.Provider = entry.Provider;
            existing.ResultsJson = entry.ResultsJson;
            existing.FetchedAt = entry.FetchedAt;
            existing.ExpiresAt = entry.ExpiresAt;
            entry = existing;
        }

        await db.SaveChangesAsync(ct);
        return Result<SeoKeywordVendorSnapshot>.Success(entry);
    }

    private static string NormalizeSeed(string seedKeyword) =>
        seedKeyword.Trim().ToLowerInvariant();
}
