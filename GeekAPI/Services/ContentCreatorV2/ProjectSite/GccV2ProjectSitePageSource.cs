using System.Text.Json;
using GeekAPI.HttpClients;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.ProjectSite;

/// <summary>
/// Where project-site crawl pages are read from: the shared <c>geek_crawler</c> Mongo store, the
/// same place every other crawl type lives. There is one implementation and no flag.
///
/// <para>
/// Until 2026-09-29 there were two, and the other one read a Postgres copy of the corpus that
/// GeekAPI crawled and wrote itself — raw page HTML in a <c>text</c> column. Both the Postgres
/// source and the in-process crawler behind it are gone, along with their tables. Railway Postgres
/// is for OAuth state only; see <c>AGENTS.md</c> § "What Postgres is for".
/// </para>
///
/// <para>
/// The interface survives the removal because it kept the consumers out of it: the seam let the
/// read path move without touching <c>GccV2SiteHierarchyFromCrawl</c>,
/// <c>GccV2ProjectSiteGrounding</c> or <c>GccV2ProjectSitePageMapper</c> — the pure functions that
/// derive grounding — and those are still untouched now that only Mongo remains.
/// </para>
/// </summary>
public interface IGccV2ProjectSitePageSource
{
    Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListPagesAsync(
        Guid runId, int limit, int offset, CancellationToken ct);

    Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListPagesBySeedsAsync(
        Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct);

    Task<GccV2ProjectSiteCrawlRunDto?> GetRunAsync(Guid runId, CancellationToken ct);

    Task<GccV2ProjectSiteCrawlPageActivityDto?> GetPageActivityAsync(Guid runId, CancellationToken ct);
}

/// <summary>
/// Reads project-site pages from the shared geek_crawler Mongo store, where every other crawl type
/// already lives.
///
/// <see cref="GeekCrawlerPageDto"/> is a strict superset of <see cref="GccV2ProjectSiteCrawlPageDto"/>
/// — it additionally carries Title, ContentHtml, Excerpt and FailureReason — so the projection is
/// lossless in the direction consumers read.
/// </summary>
public sealed class GccV2MongoProjectSitePageSource(HttpGeekCrawlerRepository crawler)
    : IGccV2ProjectSitePageSource
{
    public async Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListPagesAsync(
        Guid runId, int limit, int offset, CancellationToken ct) =>
        (await crawler.ListPagesAsync(runId, limit, offset, ct).ConfigureAwait(false))
            .Select(ToProjectSitePage)
            .ToList();

    public async Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListPagesBySeedsAsync(
        Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct) =>
        (await crawler.ListPagesBySeedsAsync(runId, seedUrls, ct).ConfigureAwait(false))
            .Select(ToProjectSitePage)
            .ToList();

    public async Task<GccV2ProjectSiteCrawlRunDto?> GetRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await crawler.GetRunAsync(runId, ct).ConfigureAwait(false);
        if (run is null) return null;

        // Only project-site runs may be read through this source. A partner or competitor run id
        // arriving here would otherwise be treated as site grounding, which is exactly the
        // cross-type confusion the crawl types exist to prevent.
        if (!string.Equals(run.CrawlType, CrawlTypes.ProjectSite, StringComparison.Ordinal))
            return null;

        // A run with no readable seed cannot be grounded against a site. Refuse it here rather than
        // letting an empty SeedUrl travel into the hierarchy build.
        var seedUrl = FirstSeedUrl(run.SeedUrlsJson);
        if (seedUrl is null) return null;

        return new GccV2ProjectSiteCrawlRunDto(
            run.Id,
            run.OwnerUserId,
            seedUrl,
            run.Status,
            run.SeedUrlsJson,
            run.HostProgressJson,
            run.ErrorSummary,
            run.CreatedAtUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc);
    }

    public async Task<GccV2ProjectSiteCrawlPageActivityDto?> GetPageActivityAsync(
        Guid runId, CancellationToken ct)
    {
        // Mongo serves activity as an indexed count plus a single Html-excluded projection for the
        // latest timestamp (MongoGeekCrawlerService.GetLastCrawledTimeAsync). Counting by paging the
        // pages themselves would pull every page's Html across the wire to produce one integer.
        var activity = await crawler.GetPageActivityAsync(runId, ct).ConfigureAwait(false);
        if (activity is null) return null;

        // null means "no such run"; PageCount 0 means "run exists, nothing crawled yet". Collapsing
        // the second into the first hides orphaned runs from stall recovery, whose orphan branch
        // tests PageCount == 0.
        return new GccV2ProjectSiteCrawlPageActivityDto(activity.PageCount, activity.LastCrawledAtUtc);
    }

    private static GccV2ProjectSiteCrawlPageDto ToProjectSitePage(GeekCrawlerPageDto p) =>
        new(p.Id, p.RunId, p.Origin, p.Url, p.FinalUrl, p.StatusCode, p.RobotsAllowed, p.Html, p.CrawledAtUtc);

    /// <summary>
    /// Project-site runs are single-seed; the seed is the site URL. Returns null when no seed can be
    /// read — a run whose site URL is unknown is not a usable project-site run, and substituting an
    /// empty string would hand consumers a success-shaped run pointing at nothing.
    /// </summary>
    private static string? FirstSeedUrl(string? seedUrlsJson)
    {
        if (string.IsNullOrWhiteSpace(seedUrlsJson)) return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(seedUrlsJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.String) continue;
                var seed = e.GetString();
                if (!string.IsNullOrWhiteSpace(seed)) return seed;
            }
        }

        return null;
    }
}
