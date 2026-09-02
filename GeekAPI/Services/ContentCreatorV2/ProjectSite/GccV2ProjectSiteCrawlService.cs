using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.ProjectSite;

public sealed class GccV2ProjectSiteCrawlService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpGccV2Repository _repo;
    private readonly GccV2ProjectSiteBfsCrawler _bfs;
    private readonly GeekCrawlerSitemapSeeder _sitemapSeeder;
    private readonly GccV2ProjectSiteCrawlWake _wake;
    private readonly GccV2ProjectSiteCrawlRunCoordinator _coordinator;
    private readonly GccV2ProjectSiteCrawlProgressNotifier _notifier;
    private readonly ILogger<GccV2ProjectSiteCrawlService> _logger;

    public GccV2ProjectSiteCrawlService(
        HttpGccV2Repository repo,
        GccV2ProjectSiteBfsCrawler bfs,
        GeekCrawlerSitemapSeeder sitemapSeeder,
        GccV2ProjectSiteCrawlWake wake,
        GccV2ProjectSiteCrawlRunCoordinator coordinator,
        GccV2ProjectSiteCrawlProgressNotifier notifier,
        ILogger<GccV2ProjectSiteCrawlService> logger)
    {
        _repo = repo;
        _bfs = bfs;
        _sitemapSeeder = sitemapSeeder;
        _wake = wake;
        _coordinator = coordinator;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<GccV2ProjectSiteCrawlRunDto?> FindInProgressRunAsync(
        string ownerUserId,
        string siteUrl,
        CancellationToken ct)
    {
        var normalized = NormalizeSiteUrl(siteUrl);
        if (normalized is null) return null;

        var latest = await _repo.GetLatestProjectSiteCrawlRunAsync(ownerUserId, normalized, ct);
        if (latest is null) return null;

        if (string.Equals(latest.Status, "running", StringComparison.OrdinalIgnoreCase)
            || string.Equals(latest.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return latest;

        return null;
    }

    public async Task<GccV2ProjectSiteCrawlRunDto> StartCrawlAsync(
        string ownerUserId,
        string siteUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            throw new InvalidOperationException("ownerUserId is required.");

        var normalized = NormalizeSiteUrl(siteUrl)
            ?? throw new InvalidOperationException("siteUrl is required.");

        var inProgress = await FindInProgressRunAsync(ownerUserId, normalized, ct);
        if (inProgress is not null)
        {
            _wake.Wake(inProgress.Id);
            await PushRunAsync(inProgress, ct).ConfigureAwait(false);
            return inProgress;
        }

        var seeds = GeekCrawlerSeedNormalizer.NormalizeSeeds([normalized]);
        var run = await _repo.CreateProjectSiteCrawlRunAsync(
            new CreateGccV2ProjectSiteCrawlRunCommand(
                ownerUserId,
                normalized,
                GeekCrawlerSeedNormalizer.SerializeSeeds(seeds)),
            ct).ConfigureAwait(false);

        await PushRunAsync(run, ct).ConfigureAwait(false);
        _wake.Wake(run.Id);
        return run;
    }

    public async Task CancelRunAsync(Guid runId, CancellationToken ct)
    {
        _coordinator.Cancel(runId);

        var run = await _repo.GetProjectSiteCrawlRunAsync(runId, ct).ConfigureAwait(false);
        if (run is null) return;

        if (string.Equals(run.Status, "complete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            return;

        run = await _repo.PatchProjectSiteCrawlRunAsync(
            runId,
            new PatchGccV2ProjectSiteCrawlRunCommand(
                Status: "cancelled",
                CompletedAtUtc: DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        await PushRunAsync(run, ct).ConfigureAwait(false);
    }

    public async Task ExecuteRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await _repo.GetProjectSiteCrawlRunAsync(runId, ct).ConfigureAwait(false);
        if (run is null) return;
        if (!string.Equals(run.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            run = await _repo.PatchProjectSiteCrawlRunAsync(
                runId,
                new PatchGccV2ProjectSiteCrawlRunCommand(
                    Status: "running",
                    StartedAtUtc: DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);
            await PushRunAsync(run, ct).ConfigureAwait(false);

            var pagesAttempted = 0;
            var pagesWithHtml = 0;

            var sitemapUrls = await _sitemapSeeder.CollectAllowedUrlsAsync(run.SiteUrl, ct)
                .ConfigureAwait(false);

            await _bfs.CrawlSiteAsync(
                run.SiteUrl,
                async batch =>
                {
                    var pageResult = await _repo.CreateProjectSiteCrawlPagesBatchAsync(
                        new CreateGccV2ProjectSiteCrawlPageBatchCommand(
                            runId,
                            batch.Select(p => new CreateGccV2ProjectSiteCrawlPageItemCommand(
                                p.Origin,
                                p.Url,
                                p.FinalUrl,
                                p.StatusCode,
                                p.RobotsAllowed,
                                p.Html)).ToList()),
                        ct).ConfigureAwait(false);

                    pagesAttempted += batch.Count;
                    pagesWithHtml += batch.Count(p => !string.IsNullOrWhiteSpace(p.Html));

                    var pageIdByUrl = pageResult.Pages.ToDictionary(
                        p => p.Url,
                        p => p.PageId,
                        StringComparer.OrdinalIgnoreCase);

                    var linkItems = new List<CreateGccV2ProjectSiteCrawlLinkItemCommand>();
                    foreach (var page in batch)
                    {
                        if (!pageIdByUrl.TryGetValue(page.Url, out var pageId)) continue;
                        foreach (var link in page.SameOriginLinks)
                        {
                            linkItems.Add(new CreateGccV2ProjectSiteCrawlLinkItemCommand(
                                pageId,
                                page.FinalUrl,
                                link,
                                true));
                        }
                    }

                    if (linkItems.Count > 0)
                    {
                        await _repo.CreateProjectSiteCrawlLinksBatchAsync(
                            new CreateGccV2ProjectSiteCrawlLinkBatchCommand(runId, linkItems),
                            ct).ConfigureAwait(false);
                    }

                    var hostProgress = JsonSerializer.Serialize(new[]
                    {
                        new
                        {
                            origin = run.SiteUrl,
                            pagesAttempted,
                            pagesWithHtml,
                        },
                    }, JsonOpts);

                    run = await _repo.PatchProjectSiteCrawlRunAsync(
                        runId,
                        new PatchGccV2ProjectSiteCrawlRunCommand(HostProgressJson: hostProgress),
                        ct).ConfigureAwait(false);
                    await PushRunAsync(run, pagesWithHtml, ct).ConfigureAwait(false);
                },
                ct,
                sitemapUrls).ConfigureAwait(false);

            if (pagesWithHtml == 0)
            {
                run = await _repo.PatchProjectSiteCrawlRunAsync(
                    runId,
                    new PatchGccV2ProjectSiteCrawlRunCommand(
                        Status: "failed",
                        ErrorSummary: "Project-site crawl finished with no HTML on any page.",
                        CompletedAtUtc: DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(false);
                await PushRunAsync(run, pagesWithHtml, ct).ConfigureAwait(false);
                return;
            }

            run = await _repo.PatchProjectSiteCrawlRunAsync(
                runId,
                new PatchGccV2ProjectSiteCrawlRunCommand(
                    Status: "complete",
                    CompletedAtUtc: DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);
            await PushRunAsync(run, pagesWithHtml, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Project-site crawl run {RunId} cancelled.", runId);
            run = await _repo.PatchProjectSiteCrawlRunAsync(
                runId,
                new PatchGccV2ProjectSiteCrawlRunCommand(
                    Status: "cancelled",
                    CompletedAtUtc: DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);
            await PushRunAsync(run, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Project-site crawl run {RunId} failed.", runId);
            run = await _repo.PatchProjectSiteCrawlRunAsync(
                runId,
                new PatchGccV2ProjectSiteCrawlRunCommand(
                    Status: "failed",
                    ErrorSummary: ex.Message,
                    CompletedAtUtc: DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);
            await PushRunAsync(run, ct).ConfigureAwait(false);
        }
    }

    public static string? NormalizeSiteUrl(string? siteUrl)
    {
        if (string.IsNullOrWhiteSpace(siteUrl)) return null;
        if (!GeekCrawlerSeedNormalizer.TryNormalizeSeedUrl(siteUrl, out var normalized))
            return null;
        return normalized;
    }

    private async Task PushRunAsync(GccV2ProjectSiteCrawlRunDto run, CancellationToken ct) =>
        await PushRunAsync(run, null, ct).ConfigureAwait(false);

    private async Task PushRunAsync(GccV2ProjectSiteCrawlRunDto run, int? pageCount, CancellationToken ct)
    {
        var payload = GccV2ProjectSiteCrawlEventMapper.MapRun(run, pageCount);
        await _notifier.PushAsync(payload, run.Id, run.OwnerUserId, ct).ConfigureAwait(false);
    }
}
