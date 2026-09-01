using System.Text.Json;
using GeekApplication.Models.GeekCrawler;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Services.GeekCrawler;

/// <summary>Backfills SeedKey, dedupes crawl slots, and ensures the unique slot index exists.</summary>
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

        var slotted = runs
            .Where(r => !string.IsNullOrWhiteSpace(r.SeedKey))
            .GroupBy(r => (r.OwnerUserId, r.CrawlType, r.SeedKey!))
            .Where(g => g.Count() > 1)
            .ToList();

        var deletedRuns = 0;
        foreach (var group in slotted)
        {
            var toDelete = group
                .OrderByDescending(r => r.CreatedAtUtc)
                .Skip(1)
                .Select(r => r.Id)
                .ToList();

            foreach (var runId in toDelete)
            {
                await db.GeekCrawlerLinks.Where(l => l.RunId == runId).ExecuteDeleteAsync(ct)
                    .ConfigureAwait(false);
                await db.GeekCrawlerPages.Where(p => p.RunId == runId).ExecuteDeleteAsync(ct)
                    .ConfigureAwait(false);
                await db.GeekCrawlerRuns.Where(r => r.Id == runId).ExecuteDeleteAsync(ct)
                    .ConfigureAwait(false);
                deletedRuns++;
            }
        }

        if (deletedRuns > 0)
            logger.LogInformation("Geek-Crawler deduped {Count} older slot run(s).", deletedRuns);

        await EnsureUniqueIndexAsync(db, logger, ct).ConfigureAwait(false);
    }

    private static async Task EnsureUniqueIndexAsync(
        GeekCrawlerDbContext db,
        ILogger logger,
        CancellationToken ct)
    {
        const string sql = """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_crawl_runs_owner_type_seed_key
            ON geek_crawler.crawl_runs ("OwnerUserId", "CrawlType", "SeedKey")
            WHERE "SeedKey" IS NOT NULL AND "SeedKey" <> '';
            """;

        await db.Database.ExecuteSqlRawAsync(sql, ct).ConfigureAwait(false);
        logger.LogInformation("Geek-Crawler slot unique index ensured.");
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
