using System.Text.Json;
using GeekApplication.Models.GeekCrawler;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Services.GeekCrawler;

/// <summary>Backfills SeedKey on crawl runs that predate the column.</summary>
public static class GeekCrawlerSeedKeyBackfill
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task ApplyAsync(GeekCrawlerDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var runs = await db.GeekCrawlerRuns.ToListAsync(ct).ConfigureAwait(false);
        var changed = 0;

        foreach (var run in runs)
        {
            if (!string.IsNullOrWhiteSpace(run.SeedKey))
                continue;

            var seeds = DeserializeSeeds(run.SeedUrlsJson);
            if (seeds.Count == 0)
                continue;

            run.SeedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(seeds);
            changed++;
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Geek-Crawler backfilled SeedKey on {Count} run(s).", changed);
        }
    }

    private static List<string> DeserializeSeeds(string? seedUrlsJson)
    {
        try
        {
            var normalized = GeekCrawlerSeedNormalizer.NormalizeSeeds(
                JsonSerializer.Deserialize<List<string>>(seedUrlsJson ?? "[]", JsonOpts) ?? []);
            return normalized.ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
