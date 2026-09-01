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
    private readonly GeekCrawlerWake _wake;
    private readonly GeekCrawlerRunCoordinator _coordinator;
    private readonly GeekCrawlerProgressNotifier _notifier;
    private readonly ILogger<GeekCrawlerService> _logger;

    public GeekCrawlerService(
        HttpGeekCrawlerRepository repo,
        SameOriginBfsCrawler bfs,
        GeekCrawlerPageBatchWriter batchWriter,
        GeekCrawlerWake wake,
        GeekCrawlerRunCoordinator coordinator,
        GeekCrawlerProgressNotifier notifier,
        ILogger<GeekCrawlerService> logger)
    {
        _repo = repo;
        _bfs = bfs;
        _batchWriter = batchWriter;
        _wake = wake;
        _coordinator = coordinator;
        _notifier = notifier;
        _logger = logger;
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

        var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(seeds);
        var seedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds(seeds);
        var type = crawlType.Trim();

        var existing = await _repo.GetRunForSlotAsync(ownerUserId, type, seedKey, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            _coordinator.Cancel(existing.Id);
            var run = await _repo.ReplaceRunAsync(existing.Id, ct).ConfigureAwait(false);
            _wake.Wake(run.Id);
            await PushRunAsync(run, currentOrigin: null, ct).ConfigureAwait(false);
            return run;
        }

        var created = await _repo.CreateRunAsync(
            new CreateGeekCrawlerRunCommand(ownerUserId, type, seedsJson, seedKey),
            ct).ConfigureAwait(false);

        await PushRunAsync(created, currentOrigin: null, ct).ConfigureAwait(false);
        _wake.Wake(created.Id);
        return created;
    }

    public async Task ExecuteRunAsync(Guid runId, CancellationToken outerCt)
    {
        var run = await _repo.GetRunAsync(runId, outerCt).ConfigureAwait(false);
        GeekCrawlerRunDto? current = null;
        IReadOnlyList<string> seeds = [];
        var hostProgress = new List<object>();
        var ct = _coordinator.Register(runId, outerCt);

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested && !outerCt.IsCancellationRequested)
        {
            _logger.LogInformation("Geek-Crawler run {RunId} cancelled for replace-on-start.", runId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Geek-Crawler run {RunId} failed.", runId);
            if (current is not null)
                await FailRunAsync(current, ex.Message, ct, hostProgress).ConfigureAwait(false);
        }
        finally
        {
            _coordinator.Unregister(runId);
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
