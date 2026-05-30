using System.Text.Json;
using GeekSeo.Persistence.Entities;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekApplication.Services.Seo;

public sealed class CompetitorInsightsService(
    IContentDocumentService documents,
    ISerpCacheRepository serpCache,
    ISerpProvider serpProvider,
    CompetitorCrawlService competitorCrawl,
    ICompetitorPageRepository competitorPages) : ICompetitorInsightsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public Task<Result<CompetitorInsightsResult>> GetForDocumentAsync(
        Guid userId, Guid documentId, CancellationToken ct = default) =>
        BuildInsightsAsync(userId, documentId, crawlIfMissing: false, ct);

    public Task<Result<CompetitorInsightsResult>> RefreshCrawlForDocumentAsync(
        Guid userId, Guid documentId, CancellationToken ct = default) =>
        BuildInsightsAsync(userId, documentId, crawlIfMissing: true, ct);

    private async Task<Result<CompetitorInsightsResult>> BuildInsightsAsync(
        Guid userId, Guid documentId, bool crawlIfMissing, CancellationToken ct)
    {
        var doc = await documents.GetAsync(userId, documentId, ct);
        if (!doc.IsSuccess || doc.Value is null)
            return Result<CompetitorInsightsResult>.Failure(doc.Error ?? "Document not found");

        var keyword = doc.Value.TargetKeyword;
        var location = string.IsNullOrWhiteSpace(doc.Value.TargetLocation)
            ? "United States"
            : doc.Value.TargetLocation;
        const string languageCode = "en";

        var serpRow = await EnsureSerpCacheAsync(keyword, location, languageCode, ct);
        if (!serpRow.IsSuccess)
            return Result<CompetitorInsightsResult>.Failure(serpRow.Error ?? "SERP error");

        if (serpRow.Value is null)
        {
            return Result<CompetitorInsightsResult>.Success(new CompetitorInsightsResult
            {
                Keyword = keyword,
                Location = location,
                Pages = [],
                BenchmarkQuality = "low_sample_count",
                CrawlStatus = "pending_serp",
            });
        }

        var benchmarks = JsonSerializer.Deserialize<SerpBenchmarksPayload>(serpRow.Value.ResultsJson, JsonOptions);
        var organic = benchmarks?.OrganicResults ?? [];

        if (crawlIfMissing && organic.Count > 0)
        {
            var serpData = new SerpResult
            {
                Keyword = keyword,
                Location = location,
                OrganicResults = organic,
                Features = JsonSerializer.Deserialize<SerpFeatures>(serpRow.Value.SerpFeaturesJson, JsonOptions)
                    ?? new SerpFeatures(),
                FetchedAt = serpRow.Value.FetchedAt,
            };
            await competitorCrawl.EnsureCompetitorPagesAsync(serpRow.Value.Id, serpData.OrganicResults, ct);
        }

        var pagesResult = await competitorPages.GetBySerpResultAsync(serpRow.Value.Id, ct);
        var crawled = pagesResult.Value ?? [];

        var insights = organic.Take(10).Select(o =>
        {
            var match = crawled.FirstOrDefault(c =>
                string.Equals(c.Url, o.Url, StringComparison.OrdinalIgnoreCase));
            return new CompetitorPageInsight
            {
                Url = o.Url,
                Domain = o.Domain ?? match?.Domain,
                Position = o.Position,
                WordCount = match?.WordCount ?? 0,
                MetaTitle = match?.MetaTitle ?? o.Title,
                CrawledAt = match?.CrawledAt,
            };
        }).ToList();

        var uncrawled = insights.Count(p => p.WordCount <= 0);
        var crawlStatus = uncrawled == 0 ? "complete" : uncrawled == insights.Count ? "pending" : "partial";

        return Result<CompetitorInsightsResult>.Success(new CompetitorInsightsResult
        {
            Keyword = keyword,
            Location = location,
            Pages = insights,
            BenchmarkQuality = benchmarks?.BenchmarkQuality ?? "low_sample_count",
            CrawlStatus = crawlStatus,
        });
    }

    private async Task<Result<SeoSerpResult?>> EnsureSerpCacheAsync(
        string keyword, string location, string languageCode, CancellationToken ct)
    {
        var cacheResult = await serpCache.GetAsync(keyword, location, languageCode, ct);
        if (!cacheResult.IsSuccess)
            return Result<SeoSerpResult?>.Failure(cacheResult.Error ?? "SERP cache error");

        if (cacheResult.Value is not null && cacheResult.Value.ExpiresAt > DateTimeOffset.UtcNow)
            return Result<SeoSerpResult?>.Success(cacheResult.Value);

        var fetch = await serpProvider.GetSerpResultsAsync(new SerpRequest
        {
            Keyword = keyword,
            Location = location,
            LanguageCode = languageCode,
            ResultCount = 10,
        }, ct);

        if (!fetch.IsSuccess || fetch.Value is null)
            return Result<SeoSerpResult?>.Failure(fetch.Error ?? "SERP fetch failed");

        var benchmarks = SerpBenchmarkCalculator.FromSerp(fetch.Value);
        var upserted = await serpCache.UpsertAsync(keyword, location, languageCode, fetch.Value, benchmarks, ct);
        if (!upserted.IsSuccess)
            return Result<SeoSerpResult?>.Failure(upserted.Error ?? "SERP cache upsert failed");

        return Result<SeoSerpResult?>.Success(upserted.Value);
    }
}
