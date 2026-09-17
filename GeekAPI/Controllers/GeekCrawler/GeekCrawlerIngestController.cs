using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.GeekCrawler;

/// <summary>
/// Ingest APIs for external crawlers (Crawlee on localhost).
/// Creates/patches runs and persists pages/links via GeekRepository → Mongo.
/// Does <b>not</b> wake the in-process .NET BFS worker.
/// </summary>
[ApiController]
[Route("api/geek-crawler/ingest")]
public class GeekCrawlerIngestController : ControllerBase
{
    /// <summary>
    /// Status used while an external crawler owns the run.
    /// Must not be <c>pending</c>/<c>running</c> so GeekCrawlerWorker / stall recovery ignore it.
    /// </summary>
    public const string ExternalStatus = GeekCrawlerRunStatuses.External;

    private const long MaxPageBatchBytes = 50L * 1024 * 1024;

    private readonly ICurrentUserContext _user;
    private readonly HttpGeekCrawlerRepository _repo;
    private readonly GeekCrawlerProgressNotifier _notifier;
    private readonly IGeekCrawlerRagClient _rag;

    public GeekCrawlerIngestController(
        ICurrentUserContext user,
        HttpGeekCrawlerRepository repo,
        GeekCrawlerProgressNotifier notifier,
        IGeekCrawlerRagClient rag)
    {
        _user = user;
        _repo = repo;
        _notifier = notifier;
        _rag = rag;
    }

    /// <summary>Create a crawl run owned by the authenticated user; mark status <c>external</c>.</summary>
    [HttpPost("runs")]
    public async Task<IActionResult> CreateRun(
        [FromBody] IngestCreateRunRequest request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request is null || !CrawlTypes.IsValid(request.CrawlType))
            return BadRequest("crawlType must be one of: competitors, partner, local.");

        var validationError = GeekCrawlerSeedNormalizer.ValidateRawSeeds(request.Seeds);
        if (validationError is not null)
            return BadRequest(validationError);

        var seeds = GeekCrawlerSeedNormalizer.NormalizeSeeds(request.Seeds);
        if (seeds.Count == 0)
            return BadRequest("At least one valid seed URL is required.");

        var ownerUserId = _user.UserId.ToString("D");
        var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(seeds);
        var seedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds(seeds);

        var crawlType = request.CrawlType.Trim();

        try
        {
            // Replace on re-crawl: one run per unique seed URL, for every crawl type.
            //
            // The in-process path already does this (GeekCrawlerService.StartCrawlAsync). This
            // controller computed seedKey and then never looked it up, so every external crawl created
            // a new run and a second full copy of the site — 1,050 project-site pages accumulated
            // across 19 runs of one site, and it is what grew the corpus past 200k pages.
            //
            // The crawler needs no change: it sends seeds and takes back a run id, and now receives
            // the same id for the same site.
            var existing = await _repo.GetRunForSlotAsync(ownerUserId, crawlType, seedKey, ct)
                .ConfigureAwait(false);

            GeekCrawlerRunDto run;
            if (existing is not null)
            {
                // Vectors first, and abort the whole operation if that fails. Deleting pages while
                // their vectors survive leaves the index pointing at rows that no longer exist —
                // same order as DeleteRun below.
                if (!_rag.IsEnabled)
                {
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        "Geek-Crawler-Rag is disabled — prior vectors cannot be purged, so the "
                        + "existing run was not replaced.");
                }

                if (!await _rag.DeleteRunIndexAsync(existing.Id, ct).ConfigureAwait(false))
                {
                    return StatusCode(
                        StatusCodes.Status502BadGateway,
                        "Vector purge failed for the existing run — nothing was replaced.");
                }

                await _repo.ClearRunCrawlDataAsync(existing.Id, ct).ConfigureAwait(false);

                run = await _repo.PatchRunAsync(
                    existing.Id,
                    new PatchGeekCrawlerRunCommand(
                        Status: ExternalStatus,
                        StartedAtUtc: DateTimeOffset.UtcNow,
                        CompletedAtUtc: null,
                        ErrorSummary: null,
                        ClearMarkdownReadyAt: true),
                    ct).ConfigureAwait(false);
            }
            else
            {
                run = await _repo.CreateRunAsync(
                    new CreateGeekCrawlerRunCommand(
                        ownerUserId,
                        crawlType,
                        seedsJson,
                        seedKey),
                    ct).ConfigureAwait(false);

                // Repo create always starts as pending — flip immediately so workers never claim it.
                run = await _repo.PatchRunAsync(
                    run.Id,
                    new PatchGeekCrawlerRunCommand(
                        Status: ExternalStatus,
                        StartedAtUtc: DateTimeOffset.UtcNow),
                    ct).ConfigureAwait(false);
            }

            var snapshot = GeekCrawlerService.ToSnapshot(run);
            await _notifier.PushAsync(snapshot, run.Id, ownerUserId, ct).ConfigureAwait(false);
            return Ok(snapshot);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    [HttpPatch("runs/{runId:guid}")]
    public async Task<IActionResult> PatchRun(
        Guid runId,
        [FromBody] IngestPatchRunRequest request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!await OwnsRunAsync(runId, ct).ConfigureAwait(false)) return NotFound();
        if (request is null)
            return BadRequest("patch body is required");
        if (request.ClearMarkdownReadyAt && request.MarkdownReadyAt is not null)
            return BadRequest("markdownReadyAt and clearMarkdownReadyAt cannot both be set");
        if (request.MarkdownReadyAt is not null
            && !string.Equals(request.Status, "complete", StringComparison.OrdinalIgnoreCase))
            return BadRequest("markdownReadyAt requires status=complete");

        if (!string.IsNullOrWhiteSpace(request.Status)
            && !IsAllowedIngestStatus(request.Status))
        {
            return BadRequest(
                "status must be one of: external, complete, failed, cancelled.");
        }

        try
        {
            var run = await _repo.PatchRunAsync(
                runId,
                new PatchGeekCrawlerRunCommand(
                    Status: request.Status,
                    ErrorSummary: request.ErrorSummary,
                    HostProgressJson: request.HostProgressJson,
                    StartedAtUtc: request.StartedAtUtc,
                    CompletedAtUtc: request.CompletedAtUtc,
                    MarkdownReadyAt: request.MarkdownReadyAt,
                    ClearMarkdownReadyAt: request.ClearMarkdownReadyAt),
                ct).ConfigureAwait(false);

            var snapshot = GeekCrawlerService.ToSnapshot(run);
            await _notifier.PushAsync(snapshot, run.Id, run.OwnerUserId, ct).ConfigureAwait(false);
            if (string.Equals(run.Status, "complete", StringComparison.OrdinalIgnoreCase)
                && _rag.IsEnabled)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _rag.EnqueueIndexAsync(run.Id).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Fire-and-forget; crawl ingest must not fail on RAG trigger.
                    }
                });
            }

            return Ok(snapshot);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// Delete a run's crawl data. Vectors are purged from Geek-Crawler-Rag first, then pages and
    /// links from Mongo: a retained vector whose Markdown source is gone would return chunks that
    /// can never be verified, so an unproven purge aborts the whole operation.
    /// </summary>
    [HttpDelete("runs/{runId:guid}")]
    public async Task<IActionResult> DeleteRun(Guid runId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!await OwnsRunAsync(runId, ct).ConfigureAwait(false)) return NotFound();

        if (!_rag.IsEnabled)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                "Geek-Crawler-Rag is disabled — vectors cannot be purged, so nothing was deleted.");
        }

        var purged = await _rag.DeleteRunIndexAsync(runId, ct).ConfigureAwait(false);
        if (!purged)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                "Vector purge failed — nothing was deleted.");
        }

        try
        {
            await _repo.ClearRunCrawlDataAsync(runId, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }

        return Ok(new { runId, vectorsPurged = true, crawlDataDeleted = true });
    }

    [HttpPost("runs/{runId:guid}/pages/batch")]
    [RequestSizeLimit(MaxPageBatchBytes)]
    public async Task<IActionResult> CreatePagesBatch(
        Guid runId,
        [FromBody] IngestPagesBatchRequest request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!await OwnsRunAsync(runId, ct).ConfigureAwait(false)) return NotFound();
        if (request?.Pages is null || request.Pages.Count == 0)
            return BadRequest("pages are required");
        if (request.Pages.Count > 100)
            return BadRequest("at most 100 pages per batch");

        var robotsDisallowedCount = request.Pages.Count(p => !p.RobotsAllowed);
        var failureReasonCount = request.Pages.Count(p =>
            p.RobotsAllowed && !string.IsNullOrWhiteSpace(p.FailureReason));
        var blankContentCount = request.Pages.Count(p =>
            p.RobotsAllowed
            && string.IsNullOrWhiteSpace(p.FailureReason)
            && string.IsNullOrWhiteSpace(p.Html)
            && string.IsNullOrWhiteSpace(p.Markdown));
        var acceptedPages = request.Pages.Where(p =>
            p.RobotsAllowed
            && string.IsNullOrWhiteSpace(p.FailureReason)
            && (!string.IsNullOrWhiteSpace(p.Html) || !string.IsNullOrWhiteSpace(p.Markdown)))
            .ToList();
        var rejectedCount = request.Pages.Count - acceptedPages.Count;

        var items = acceptedPages.Select(p => new CreateGeekCrawlerPageItemCommand(
            p.Origin ?? "",
            p.Url ?? "",
            p.FinalUrl,
            p.StatusCode,
            p.RobotsAllowed,
            p.Html,
            p.FailureReason,
            p.Title,
            p.Markdown,
            p.Excerpt)).ToList();

        try
        {
            var result = items.Count == 0
                ? new GeekCrawlerPageBatchResult(0, [])
                : await _repo.CreatePagesBatchAsync(
                    new CreateGeekCrawlerPageBatchCommand(runId, items),
                    ct).ConfigureAwait(false);

            // Lightweight progress ping (no HTML) so operator UI can refresh URL counts.
            var run = await _repo.GetRunAsync(runId, ct).ConfigureAwait(false);
            if (run is not null)
            {
                await _notifier.PushAsync(
                    new
                    {
                        runId = run.Id,
                        status = run.Status,
                        crawlType = run.CrawlType,
                        eventType = "pages_batch",
                        pagesInBatch = result.Count,
                    },
                    run.Id,
                    run.OwnerUserId,
                    ct).ConfigureAwait(false);
            }

            return Ok(new
            {
                result.Count,
                result.Pages,
                rejectedCount,
                rejectedReasonCounts = new
                {
                    robotsDisallowed = robotsDisallowedCount,
                    failureReason = failureReasonCount,
                    blankContent = blankContentCount,
                },
            });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    [HttpPost("runs/{runId:guid}/links/batch")]
    public async Task<IActionResult> CreateLinksBatch(
        Guid runId,
        [FromBody] IngestLinksBatchRequest request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!await OwnsRunAsync(runId, ct).ConfigureAwait(false)) return NotFound();
        if (request?.Links is null || request.Links.Count == 0)
            return BadRequest("links are required");
        if (request.Links.Count > 2000)
            return BadRequest("at most 2000 links per batch");

        var items = request.Links.Select(l => new CreateGeekCrawlerLinkItemCommand(
            l.PageId,
            l.FromUrl ?? "",
            l.LinkUrl ?? "",
            l.IsSameOrigin)).ToList();

        try
        {
            await _repo.CreateLinksBatchAsync(
                new CreateGeekCrawlerLinkBatchCommand(runId, items),
                ct).ConfigureAwait(false);
            return Ok(new { count = items.Count });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    private async Task<bool> OwnsRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await _repo.GetRunAsync(runId, ct).ConfigureAwait(false);
        return run is not null
               && string.Equals(run.OwnerUserId, _user.UserId.ToString("D"), StringComparison.Ordinal);
    }

    private static bool IsAllowedIngestStatus(string status) =>
        status.Equals(ExternalStatus, StringComparison.OrdinalIgnoreCase)
        || status.Equals("complete", StringComparison.OrdinalIgnoreCase)
        || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
        || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase);

    public record IngestCreateRunRequest(string CrawlType, string[]? Seeds);

    public record IngestPatchRunRequest(
        string? Status = null,
        string? ErrorSummary = null,
        string? HostProgressJson = null,
        DateTimeOffset? StartedAtUtc = null,
        DateTimeOffset? CompletedAtUtc = null,
        DateTimeOffset? MarkdownReadyAt = null,
        bool ClearMarkdownReadyAt = false);

    public record IngestPagesBatchRequest(IReadOnlyList<IngestPageItem>? Pages);

    public record IngestPageItem(
        string? Origin,
        string? Url,
        string? FinalUrl,
        int StatusCode,
        bool RobotsAllowed,
        string? Html,
        string? FailureReason = null,
        string? Title = null,
        string? Markdown = null,
        string? Excerpt = null);

    public record IngestLinksBatchRequest(IReadOnlyList<IngestLinkItem>? Links);

    public record IngestLinkItem(
        Guid PageId,
        string? FromUrl,
        string? LinkUrl,
        bool IsSameOrigin);
}
