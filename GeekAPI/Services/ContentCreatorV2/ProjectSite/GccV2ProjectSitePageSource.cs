using System.Text.Json;
using GeekAPI.HttpClients;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.ProjectSite;

/// <summary>
/// Where project-site crawl pages are read from.
///
/// Project-site pages currently live in Postgres (<c>content_creator_v2.gcc_v2_project_site_crawl_*</c>)
/// while every other crawl type lives in the shared geek_crawler Mongo store. That split is the
/// thing being removed: GeekAPI is a service layer and should not own a crawl corpus, least of all
/// raw page HTML in a <c>text</c> column.
///
/// This seam lets the read path move first, behind a flag, without touching a single consumer's
/// logic. Both implementations return the existing <see cref="GccV2ProjectSiteCrawlPageDto"/> so
/// GccV2SiteHierarchyFromCrawl, GccV2ProjectSiteGrounding and GccV2ProjectSitePageMapper — the pure
/// functions that derive grounding — stay untouched.
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

/// <summary>Reads from the Postgres project-site tables. The path being retired.</summary>
public sealed class GccV2PostgresProjectSitePageSource(HttpGccV2Repository repo)
    : IGccV2ProjectSitePageSource
{
    public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListPagesAsync(
        Guid runId, int limit, int offset, CancellationToken ct) =>
        repo.ListProjectSiteCrawlPagesAsync(runId, limit, offset, ct);

    public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListPagesBySeedsAsync(
        Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct) =>
        repo.ListProjectSiteCrawlPagesBySeedsAsync(runId, seedUrls, ct);

    public Task<GccV2ProjectSiteCrawlRunDto?> GetRunAsync(Guid runId, CancellationToken ct) =>
        repo.GetProjectSiteCrawlRunAsync(runId, ct);

    public Task<GccV2ProjectSiteCrawlPageActivityDto?> GetPageActivityAsync(
        Guid runId, CancellationToken ct) =>
        repo.GetProjectSiteCrawlPageActivityAsync(runId, ct);
}

/// <summary>
/// Reads project-site pages from the shared geek_crawler Mongo store, where every other crawl type
/// already lives.
///
/// <see cref="GeekCrawlerPageDto"/> is a strict superset of <see cref="GccV2ProjectSiteCrawlPageDto"/>
/// — it additionally carries Title, Markdown, Excerpt and FailureReason — so the projection is
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

        return new GccV2ProjectSiteCrawlRunDto(
            run.Id,
            run.OwnerUserId,
            FirstSeedUrl(run.SeedUrlsJson),
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
        // Mongo has no activity endpoint; derive it from the pages themselves. Paged rather than
        // loaded whole — a project-site run can reach the 2500-page cap.
        var count = 0;
        DateTimeOffset? last = null;
        const int batch = 100;
        for (var offset = 0; ; offset += batch)
        {
            var page = await crawler.ListPagesAsync(runId, batch, offset, ct).ConfigureAwait(false);
            if (page.Count == 0) break;
            count += page.Count;
            foreach (var p in page)
                if (last is null || p.CrawledAtUtc > last) last = p.CrawledAtUtc;
            if (page.Count < batch) break;
        }

        return count == 0 ? null : new GccV2ProjectSiteCrawlPageActivityDto(count, last);
    }

    private static GccV2ProjectSiteCrawlPageDto ToProjectSitePage(GeekCrawlerPageDto p) =>
        new(p.Id, p.RunId, p.Origin, p.Url, p.FinalUrl, p.StatusCode, p.RobotsAllowed, p.Html, p.CrawledAtUtc);

    /// <summary>Project-site runs are single-seed; the seed is the site URL.</summary>
    private static string FirstSeedUrl(string? seedUrlsJson)
    {
        if (string.IsNullOrWhiteSpace(seedUrlsJson)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(seedUrlsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return string.Empty;
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                    return e.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
        return string.Empty;
    }
}
