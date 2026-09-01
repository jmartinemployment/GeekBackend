using System.Text.Json;
using GeekAPI.HttpClients;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.GeekCrawler;

public sealed class GeekCrawlerService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpGeekCrawlerRepository _repo;
    private readonly SameOriginBfsCrawler _bfs;
    private readonly GeekCrawlerPageBatchWriter _batchWriter;
    private readonly GeekCrawlerLinkRebuilder _linkRebuilder;
    private readonly GeekCrawlerWake _wake;
    private readonly GeekCrawlerProgressNotifier _notifier;
    private readonly ILogger<GeekCrawlerService> _logger;

    public GeekCrawlerService(
        HttpGeekCrawlerRepository repo,
        SameOriginBfsCrawler bfs,
        GeekCrawlerPageBatchWriter batchWriter,
        GeekCrawlerLinkRebuilder linkRebuilder,
        GeekCrawlerWake wake,
        GeekCrawlerProgressNotifier notifier,
        ILogger<GeekCrawlerService> logger)
    {
        _repo = repo;
        _bfs = bfs;
        _batchWriter = batchWriter;
        _linkRebuilder = linkRebuilder;
        _wake = wake;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<GeekCrawlerRunDto?> FindInProgressRunAsync(
        string ownerUserId,
        string crawlType,
        IReadOnlyList<string> seeds,
        CancellationToken ct)
    {
        var runs = await _repo.ListRunsForUserAsync(ownerUserId, crawlType, 50, ct).ConfigureAwait(false);
        return runs.FirstOrDefault(r =>
            GeekCrawlerSeedNormalizer.SeedUrlsMatch(r.SeedUrlsJson, seeds)
            && (string.Equals(r.Status, "running", StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Status, "pending", StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<GeekCrawlerRunDto?> FindLatestMatchingRunAsync(
        string ownerUserId,
        string crawlType,
        IReadOnlyList<string> seeds,
        CancellationToken ct)
    {
        var runs = await _repo.ListRunsForUserAsync(ownerUserId, crawlType, 50, ct).ConfigureAwait(false);
        return runs
            .Where(r => GeekCrawlerSeedNormalizer.SeedUrlsMatch(r.SeedUrlsJson, seeds))
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefault();
    }

    public async Task<GeekCrawlerRunDto> StartCrawlAsync(
        string ownerUserId,
        string crawlType,
        IReadOnlyList<string> seeds,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            throw new InvalidOperationException("ownerUserId is required.");
        if (!CrawlTypes.IsValid(crawlType))
            throw new InvalidOperationException("Invalid crawlType.");
        if (seeds.Count == 0)
            throw new InvalidOperationException("At least one seed URL is required.");

        var inProgress = await FindInProgressRunAsync(ownerUserId, crawlType, seeds, ct).ConfigureAwait(false);
        if (inProgress is not null)
        {
            inProgress = await TryRecoverOrphanAsync(inProgress, ct).ConfigureAwait(false);
            _wake.Wake(inProgress.Id);
            await PushRunAsync(inProgress, currentOrigin: null, ct).ConfigureAwait(false);
            return inProgress;
        }

        var latest = await FindLatestMatchingRunAsync(ownerUserId, crawlType, seeds, ct).ConfigureAwait(false);
        if (latest is not null
            && string.Equals(latest.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Resuming failed Geek-Crawler run {RunId} for matching seeds.",
                latest.Id);
            latest = await _repo.PatchRunAsync(
                latest.Id,
                new PatchGeekCrawlerRunCommand(
                    Status: "pending",
                    ErrorSummary: "",
                    CompletedAtUtc: null),
                ct).ConfigureAwait(false);
            _wake.Wake(latest.Id);
            await PushRunAsync(latest, currentOrigin: null, ct).ConfigureAwait(false);
            return latest;
        }

        var run = await _repo.CreateRunAsync(
            new CreateGeekCrawlerRunCommand(
                ownerUserId,
                crawlType.Trim(),
                GeekCrawlerSeedNormalizer.SerializeSeeds(seeds),
                GeekCrawlerSeedNormalizer.ComputeSeedKey(seeds)),
            ct).ConfigureAwait(false);

        await PushRunAsync(run, currentOrigin: null, ct).ConfigureAwait(false);
        _wake.Wake(run.Id);
        return run;
    }

    public async Task<int> RebuildLinksAsync(Guid runId, CancellationToken ct) =>
        await _linkRebuilder.RebuildMissingLinksAsync(runId, ct).ConfigureAwait(false);

    public async Task ExecuteRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await _repo.GetRunAsync(runId, ct).ConfigureAwait(false);
        GeekCrawlerRunDto? current = null;
        IReadOnlyList<string> seeds = [];
        var hostProgress = new List<object>();

        try
        {
            current = run;
            if (current is null) return;
            if (!string.Equals(current.Status, "pending", StringComparison.OrdinalIgnoreCase))
                return;

            seeds = JsonSerializer.Deserialize<List<string>>(current.SeedUrlsJson, JsonOpts) ?? [];
            if (seeds.Count == 0)
            {
                await FailRunAsync(current, "No seed URLs on crawl run.", ct).ConfigureAwait(false);
                return;
            }

            current = await _repo.PatchRunAsync(
                runId,
                new PatchGeekCrawlerRunCommand(
                    Status: "running",
                    StartedAtUtc: DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);
            await PushRunAsync(current, currentOrigin: null, ct).ConfigureAwait(false);

            await _linkRebuilder.RebuildMissingLinksAsync(runId, ct).ConfigureAwait(false);

            var hostGroups = GeekCrawlerSeedNormalizer.GroupSeedsByOrigin(seeds);
            GeekCrawlerRunResumeLoader.ResumeState? resume = null;
            var activity = await _repo.GetPageActivityAsync(runId, ct).ConfigureAwait(false);
            if (activity is { PageCount: > 0 })
            {
                resume = await GeekCrawlerRunResumeLoader.LoadAsync(
                    _repo,
                    runId,
                    hostGroups.Keys.ToList(),
                    ct).ConfigureAwait(false);
            }

            Dictionary<string, OriginProgressStats> originStats = resume is not null
                ? GeekCrawlerHostProgress.FromResumeStats(resume.OriginStats)
                : hostGroups.Keys.ToDictionary(
                    o => o,
                    _ => new OriginProgressStats(),
                    StringComparer.OrdinalIgnoreCase);

            if (resume is not null)
            {
                hostProgress = GeekCrawlerHostProgress.BuildHostProgress(hostGroups.Keys, originStats);
                current = await _repo.PatchRunAsync(
                    runId,
                    new PatchGeekCrawlerRunCommand(
                        HostProgressJson: JsonSerializer.Serialize(hostProgress, JsonOpts)),
                    ct).ConfigureAwait(false);
                await PushRunAsync(current, currentOrigin: null, ct).ConfigureAwait(false);
            }

            foreach (var (origin, originSeeds) in hostGroups)
            {
                ct.ThrowIfCancellationRequested();
                if (!originStats.ContainsKey(origin))
                    originStats[origin] = new OriginProgressStats();

                GeekCrawlerBfsResume? originResume = null;
                if (resume is not null && resume.OriginResume.TryGetValue(origin, out var loaded))
                    originResume = loaded;

                await _bfs.CrawlOriginAsync(
                    origin,
                    originSeeds,
                    async batch =>
                    {
                        var pageResult = await _batchWriter.SavePagesWithRetryAsync(runId, batch, ct)
                            .ConfigureAwait(false);

                        var pageIdByUrl = pageResult.Pages.ToDictionary(
                            p => p.Url,
                            p => p.PageId,
                            StringComparer.OrdinalIgnoreCase);

                        var linkItems = new List<CreateGeekCrawlerLinkItemCommand>();
                        foreach (var page in batch)
                        {
                            if (!pageIdByUrl.TryGetValue(page.Url, out var pageId)) continue;
                            foreach (var link in page.Links)
                            {
                                linkItems.Add(new CreateGeekCrawlerLinkItemCommand(
                                    pageId,
                                    page.FinalUrl,
                                    link.LinkUrl,
                                    link.IsSameOrigin));
                            }
                        }

                        await _batchWriter.SaveLinksWithRetryAsync(runId, linkItems, ct).ConfigureAwait(false);

                        var stats = originStats[origin];
                        foreach (var page in batch)
                            stats.AddPage(page.StatusCode, !string.IsNullOrWhiteSpace(page.Html));

                        hostProgress = GeekCrawlerHostProgress.BuildHostProgress(hostGroups.Keys, originStats);
                        current = await _repo.PatchRunAsync(
                            runId,
                            new PatchGeekCrawlerRunCommand(
                                HostProgressJson: JsonSerializer.Serialize(hostProgress, JsonOpts)),
                            ct).ConfigureAwait(false);
                        await PushRunAsync(current, origin, ct).ConfigureAwait(false);
                    },
                    ct,
                    originResume).ConfigureAwait(false);
            }

            hostProgress = GeekCrawlerHostProgress.BuildHostProgress(hostGroups.Keys, originStats);

            if (GeekCrawlerHostProgress.AllOriginsHaveZeroHtml(originStats))
            {
                await FailRunAsync(
                    current,
                    "Crawl finished with no HTML on any page.",
                    ct,
                    hostProgress).ConfigureAwait(false);
                return;
            }

            current = await _repo.PatchRunAsync(
                runId,
                new PatchGeekCrawlerRunCommand(
                    Status: "complete",
                    HostProgressJson: JsonSerializer.Serialize(hostProgress, JsonOpts),
                    CompletedAtUtc: DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);
            await PushRunAsync(current, currentOrigin: null, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Geek-Crawler run {RunId} complete for user {OwnerUserId}.",
                runId,
                current.OwnerUserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Geek-Crawler run {RunId} failed.", runId);
            if (current is not null)
                await FailRunAsync(current, ex.Message, ct, hostProgress).ConfigureAwait(false);
        }
    }

    public static GeekCrawlerRunSnapshot ToSnapshot(GeekCrawlerRunDto run)
    {
        List<string> seedUrls;
        try
        {
            seedUrls = JsonSerializer.Deserialize<List<string>>(run.SeedUrlsJson, JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            seedUrls = [];
        }

        List<object> hosts = [];
        if (!string.IsNullOrWhiteSpace(run.HostProgressJson))
        {
            try
            {
                hosts = JsonSerializer.Deserialize<List<object>>(run.HostProgressJson, JsonOpts) ?? [];
            }
            catch (JsonException)
            {
                // leave empty
            }
        }

        return new GeekCrawlerRunSnapshot(
            run.Id,
            run.CrawlType,
            run.Status,
            seedUrls,
            hosts,
            run.ErrorSummary,
            run.CreatedAtUtc,
            run.StartedAtUtc,
            run.CompletedAtUtc);
    }

    private async Task FailRunAsync(
        GeekCrawlerRunDto run,
        string message,
        CancellationToken ct,
        List<object>? hostProgress = null)
    {
        var summary = message.Length > 2048 ? message[..2048] : message;
        await _repo.PatchRunAsync(
            run.Id,
            new PatchGeekCrawlerRunCommand(
                Status: "failed",
                ErrorSummary: summary,
                HostProgressJson: hostProgress is null
                    ? run.HostProgressJson
                    : JsonSerializer.Serialize(hostProgress, JsonOpts),
                CompletedAtUtc: DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);

        var failed = await _repo.GetRunAsync(run.Id, ct).ConfigureAwait(false);
        if (failed is not null)
            await PushRunAsync(failed, currentOrigin: null, ct).ConfigureAwait(false);
    }

    private async Task<GeekCrawlerRunDto> TryRecoverOrphanAsync(
        GeekCrawlerRunDto run,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var activity = await _repo.GetPageActivityAsync(run.Id, ct).ConfigureAwait(false);
        if (activity is null)
            return run;

        var shouldRecover = activity.PageCount == 0
            ? GeekCrawlerRecovery.ShouldRecoverRunningOrphan(run, now, hasSavedPages: false)
            : activity.LastCrawledAtUtc is DateTimeOffset last
                && GeekCrawlerRecovery.ShouldRecoverStalledRunning(run, now, last);

        if (!shouldRecover)
            return run;

        _logger.LogInformation(
            "Recovering stalled Geek-Crawler run {RunId} ({PageCount} pages) back to pending.",
            run.Id,
            activity.PageCount);
        return await _repo.PatchRunAsync(
            run.Id,
            new PatchGeekCrawlerRunCommand(Status: "pending"),
            ct).ConfigureAwait(false);
    }

    private async Task PushRunAsync(GeekCrawlerRunDto run, string? currentOrigin, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(run.OwnerUserId)) return;

        try
        {
            await _notifier.PushAsync(
                GeekCrawlerEventMapper.MapRun(run, currentOrigin),
                run.Id,
                run.OwnerUserId,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Geek-Crawler push failed for run {RunId}.", run.Id);
        }
    }
}

public record GeekCrawlerRunSnapshot(
    Guid RunId,
    string CrawlType,
    string Status,
    IReadOnlyList<string> SeedUrls,
    IReadOnlyList<object> Hosts,
    string? ErrorSummary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);
