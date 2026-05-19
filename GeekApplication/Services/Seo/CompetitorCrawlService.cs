using System.Text.Json;
using GeekApplication.Entities.Seo;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class CompetitorCrawlService(
    ICrawlerProvider crawler,
    ICompetitorPageRepository competitorPages)
{
    private const int MaxConcurrentCrawls = 3;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<Result<IReadOnlyList<SeoCompetitorPage>>> EnsureCompetitorPagesAsync(
        Guid serpResultId,
        IReadOnlyList<SerpOrganicResult> organicResults,
        CancellationToken ct = default)
    {
        var cached = await competitorPages.GetBySerpResultAsync(serpResultId, ct);
        if (!cached.IsSuccess)
            return Result<IReadOnlyList<SeoCompetitorPage>>.Failure(cached.Error ?? "Competitor cache error");

        var now = DateTimeOffset.UtcNow;
        var validCached = (cached.Value ?? [])
            .Where(p => p.ExpiresAt > now)
            .ToList();

        var urlsToCrawl = organicResults
            .Take(5)
            .Select(o => o.Url)
            .Where(url => validCached.All(c => !string.Equals(c.Url, url, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var results = new List<SeoCompetitorPage>(validCached);
        using var semaphore = new SemaphoreSlim(MaxConcurrentCrawls);

        var crawlTasks = urlsToCrawl.Select(async url =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                if (!await crawler.IsAllowedByRobotsTxtAsync(url, ct))
                    return;

                var crawled = await crawler.CrawlPageAsync(url, ct);
                if (!crawled.IsSuccess || crawled.Value is null)
                    return;

                var saved = await competitorPages.UpsertAsync(serpResultId, crawled.Value, ct);
                if (saved.IsSuccess && saved.Value is not null)
                    lock (results) { results.Add(saved.Value); }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(crawlTasks);
        return Result<IReadOnlyList<SeoCompetitorPage>>.Success(results);
    }

    public static SerpBenchmarksPayload BenchmarksFromCompetitors(
        SerpResult serp,
        IReadOnlyList<SeoCompetitorPage> pages,
        SerpBenchmarksPayload fallback)
    {
        var crawled = pages.Where(p => p.WordCount > 0).ToList();
        if (crawled.Count < 3)
        {
            return fallback with { BenchmarkQuality = "low_sample_count" };
        }

        var avgWordCount = (int)crawled.Average(p => p.WordCount);
        var avgTitleLength = crawled.Where(p => p.MetaTitle is not null).Select(p => p.MetaTitle!.Length).DefaultIfEmpty(fallback.AvgTitleLength).Average();

        return fallback with
        {
            AvgWordCount = Math.Max(800, avgWordCount),
            AvgTitleLength = (int)avgTitleLength,
            BenchmarkQuality = "good",
            TopDomains = serp.OrganicResults.Take(10).Select(o => o.Domain ?? string.Empty).Where(d => d.Length > 0).ToList(),
            OrganicResults = serp.OrganicResults.Take(10).ToList(),
        };
    }
}
