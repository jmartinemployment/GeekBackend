using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.ToolSources;

public sealed class GccV2ToolSourceCrawlService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpGccV2Repository _repo;
    private readonly GccV2SameOriginBfsCrawler _bfs;
    private readonly GccV2ToolSourceCrawlWake _wake;
    private readonly GccV2JobWake _jobWake;
    private readonly GccV2CrawlProgressNotifier _crawlNotifier;
    private readonly ILogger<GccV2ToolSourceCrawlService> _logger;

    public GccV2ToolSourceCrawlService(
        HttpGccV2Repository repo,
        GccV2SameOriginBfsCrawler bfs,
        GccV2ToolSourceCrawlWake wake,
        GccV2JobWake jobWake,
        GccV2CrawlProgressNotifier crawlNotifier,
        ILogger<GccV2ToolSourceCrawlService> logger)
    {
        _repo = repo;
        _bfs = bfs;
        _wake = wake;
        _jobWake = jobWake;
        _crawlNotifier = crawlNotifier;
        _logger = logger;
    }

    public async Task<GccV2ToolSourceCrawlRunDto?> GetLatestRunAsync(Guid createId, CancellationToken ct) =>
        await _repo.GetLatestToolSourceCrawlRunAsync(createId, ct).ConfigureAwait(false);

    public async Task<GccV2ToolSourceCrawlRunDto> StartCrawlAsync(
        Guid createId,
        string? rawBriefJson,
        bool force,
        CancellationToken ct)
    {
        var seeds = GccV2PartnerUrlResearchService.CollectOperatorSeedUrls(rawBriefJson);
        if (seeds.Count == 0)
            throw new InvalidOperationException("No operator tool URLs on the brief — nothing to crawl.");

        var latest = await _repo.GetLatestToolSourceCrawlRunAsync(createId, ct).ConfigureAwait(false);
        if (latest is not null)
        {
            if (string.Equals(latest.Status, "running", StringComparison.OrdinalIgnoreCase)
                || string.Equals(latest.Status, "pending", StringComparison.OrdinalIgnoreCase))
            {
                return latest;
            }

            if (!force && string.Equals(latest.Status, "complete", StringComparison.OrdinalIgnoreCase))
                return latest;
        }

        var run = await _repo.CreateToolSourceCrawlRunAsync(
            new CreateGccV2ToolSourceCrawlRunCommand(createId, JsonSerializer.Serialize(seeds, JsonOpts)),
            ct).ConfigureAwait(false);

        await PushRunAsync(run, currentOrigin: null, ct).ConfigureAwait(false);
        _wake.Wake(run.Id);
        return run;
    }

    public async Task ExecuteRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await _repo.GetToolSourceCrawlRunAsync(runId, ct).ConfigureAwait(false);
        GccV2ToolSourceCrawlRunDto? current = null;
        try
        {
            current = run;
            if (current is null) return;

            if (!string.Equals(current.Status, "pending", StringComparison.OrdinalIgnoreCase))
                return;

            var seeds = JsonSerializer.Deserialize<List<string>>(current.SeedUrlsJson, JsonOpts) ?? [];
            if (seeds.Count == 0)
            {
                await FailRunAsync(current, "No seed URLs on crawl run.", ct).ConfigureAwait(false);
                return;
            }

            current = await _repo.PatchToolSourceCrawlRunAsync(
                runId,
                new PatchGccV2ToolSourceCrawlRunCommand(
                    Status: "running",
                    StartedAtUtc: DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);
            await PushRunAsync(current, currentOrigin: null, ct).ConfigureAwait(false);

            var hostGroups = GccV2PartnerUrlResearchService.GroupOperatorSeedsByOrigin(seeds);
            var hostProgress = new List<object>();
            var quotePages = new List<GccQuoteablePage>();

            foreach (var (origin, originSeeds) in hostGroups)
            {
                ct.ThrowIfCancellationRequested();
                var crawled = await _bfs.CrawlOriginAsync(origin, originSeeds, ct).ConfigureAwait(false);
                var withHtml = crawled.Where(p => !string.IsNullOrWhiteSpace(p.Html)).ToList();

                foreach (var batch in crawled.Chunk(20))
                {
                    await _repo.CreateToolSourceCrawlPagesBatchAsync(
                        new CreateGccV2ToolSourceCrawlPageBatchCommand(
                            runId,
                            batch.Select(p => new CreateGccV2ToolSourceCrawlPageItemCommand(
                                p.Origin,
                                p.Url,
                                p.FinalUrl,
                                p.StatusCode,
                                p.RobotsAllowed,
                                p.Html)).ToList()),
                        ct).ConfigureAwait(false);
                }

                var originQuoteCount = 0;
                foreach (var page in withHtml)
                {
                    var extracted = GccV2ArticleHtmlExtractor.ExtractPartnerPage(page.Url, page.Html!);
                    if (GccV2ArticleHtmlExtractor.IsEmpty(extracted)) continue;
                    quotePages.Add(extracted);
                    originQuoteCount++;

                    var pageJson = JsonSerializer.Serialize(extracted, JsonOpts);
                    await _repo.CreatePartnerResearchRecordAsync(
                        new CreateGccV2PartnerResearchRecordCommand(
                            current.CreateId,
                            page.Url,
                            true,
                            page.Status,
                            HostDomain: new Uri(page.Url).Host,
                            PageJson: pageJson,
                            FlattenedTextContent: FlattenPage(extracted)),
                        ct).ConfigureAwait(false);
                }

                hostProgress.Add(new
                {
                    origin,
                    pagesAttempted = crawled.Count,
                    pagesWithHtml = withHtml.Count,
                    quotePages = originQuoteCount,
                });

                current = await _repo.PatchToolSourceCrawlRunAsync(
                    runId,
                    new PatchGccV2ToolSourceCrawlRunCommand(
                        HostProgressJson: JsonSerializer.Serialize(hostProgress, JsonOpts)),
                    ct).ConfigureAwait(false);
                await PushRunAsync(current, origin, ct).ConfigureAwait(false);
            }

            if (quotePages.Count == 0)
            {
                await FailRunAsync(
                    current,
                    $"Crawl finished but no quoteable passages were extracted from {seeds.Count} seed URL(s).",
                    ct,
                    hostProgress).ConfigureAwait(false);
                await WakeToolJobsAsync(current.CreateId, ct).ConfigureAwait(false);
                return;
            }

            var partnerResearchJson = JsonSerializer.Serialize(quotePages, JsonOpts);
            current = await _repo.PatchToolSourceCrawlRunAsync(
                runId,
                new PatchGccV2ToolSourceCrawlRunCommand(
                    Status: "complete",
                    HostProgressJson: JsonSerializer.Serialize(hostProgress, JsonOpts),
                    PartnerResearchJson: partnerResearchJson,
                    CompletedAtUtc: DateTimeOffset.UtcNow),
                ct).ConfigureAwait(false);
            await PushRunAsync(current, currentOrigin: null, ct).ConfigureAwait(false);

            await MergeResearchOntoCreateBriefsAsync(current.CreateId, quotePages, runId, hostProgress, ct)
                .ConfigureAwait(false);
            await WakeToolJobsAsync(current.CreateId, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Tool source crawl {RunId} complete for create {CreateId}: {QuoteCount} quote page(s).",
                runId,
                current.CreateId,
                quotePages.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Tool source crawl {RunId} failed.", runId);
            if (current is not null)
                await FailRunAsync(current, ex.Message, ct).ConfigureAwait(false);
            if (current is not null)
                await WakeToolJobsAsync(current.CreateId, ct).ConfigureAwait(false);
        }
    }

    public static IReadOnlyList<GccQuoteablePage> DeserializePartnerResearch(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<GccQuoteablePage>>(json, JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task FailRunAsync(
        GccV2ToolSourceCrawlRunDto run,
        string error,
        CancellationToken ct,
        IReadOnlyList<object>? hostProgress = null)
    {
        var patched = await _repo.PatchToolSourceCrawlRunAsync(
            run.Id,
            new PatchGccV2ToolSourceCrawlRunCommand(
                Status: "failed",
                ErrorSummary: error.Length > 2048 ? error[..2048] : error,
                HostProgressJson: hostProgress is null ? null : JsonSerializer.Serialize(hostProgress, JsonOpts),
                CompletedAtUtc: DateTimeOffset.UtcNow),
            ct).ConfigureAwait(false);
        await PushRunAsync(patched, currentOrigin: null, ct).ConfigureAwait(false);
    }

    private async Task PushRunAsync(
        GccV2ToolSourceCrawlRunDto run,
        string? currentOrigin,
        CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(run.CreateId, ct).ConfigureAwait(false);
        if (create is null) return;

        await _crawlNotifier.PushAsync(
            GccV2ToolSourceCrawlEventMapper.MapRun(run, currentOrigin),
            run.Id,
            create.OwnerUserId,
            ct).ConfigureAwait(false);
    }

    private async Task MergeResearchOntoCreateBriefsAsync(
        Guid createId,
        IReadOnlyList<GccQuoteablePage> quotePages,
        Guid runId,
        IReadOnlyList<object> hostProgress,
        CancellationToken ct)
    {
        var briefs = await _repo.ListBriefsByCreateAsync(createId, ct).ConfigureAwait(false);
        if (briefs.Count == 0) return;

        var statusObj = new
        {
            runId,
            status = "complete",
            hosts = hostProgress,
        };

        foreach (var brief in briefs)
        {
            var merged = GccV2PartnerUrlResearchService.MergePartnerResearchIntoBriefJson(
                brief.RawBriefJson,
                quotePages);
            merged = GccV2PartnerUrlResearchService.MergeToolSourceCrawlIntoBriefJson(merged, statusObj);
            if (merged is null) continue;
            await _repo.PatchBriefAsync(brief.Id, new PatchGccV2BriefCommand(merged), ct).ConfigureAwait(false);
        }
    }

    private async Task WakeToolJobsAsync(Guid createId, CancellationToken ct)
    {
        var jobs = await _repo.ListJobsByCreateAsync(createId, ct).ConfigureAwait(false);
        foreach (var job in jobs.Where(j =>
                     string.Equals(j.ContentType, "tool", StringComparison.OrdinalIgnoreCase)
                     && !IsTerminal(j.Status)))
        {
            _jobWake.Wake(job.Id);
        }
    }

    private static bool IsTerminal(string status) =>
        string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

    private static string FlattenPage(GccQuoteablePage page)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(page.Title))
            sb.AppendLine(page.Title);
        foreach (var h in page.Headings)
            sb.AppendLine($"H{h.Level}: {h.Text}");
        foreach (var p in page.Paragraphs)
            sb.AppendLine(p);
        return sb.ToString();
    }
}
