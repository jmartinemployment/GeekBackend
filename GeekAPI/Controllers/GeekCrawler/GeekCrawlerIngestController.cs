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
    private readonly ILogger<GeekCrawlerIngestController> _logger;

    public GeekCrawlerIngestController(
        ICurrentUserContext user,
        HttpGeekCrawlerRepository repo,
        GeekCrawlerProgressNotifier notifier,
        IGeekCrawlerRagClient rag,
        ILogger<GeekCrawlerIngestController> logger)
    {
        _user = user;
        _repo = repo;
        _notifier = notifier;
        _rag = rag;
        _logger = logger;
    }

    /// <summary>Create a crawl run owned by the authenticated user; mark status <c>external</c>.</summary>
    [HttpPost("runs")]
    public async Task<IActionResult> CreateRun(
        [FromBody] IngestCreateRunRequest request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request is null || !CrawlTypes.IsValid(request.CrawlType))
            return BadRequest("crawlType must be one of: competitors, partner, local, project-site.");

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
            // Atomic publish. A crawl writes into its own staging run; the run currently published
            // for this slot is never touched while that crawl is in flight.
            //
            // The previous shape purged vectors and called ClearRunCrawlDataAsync up front and
            // re-used the same run id, so a crawl that died at page 3 of 2,500 had already destroyed
            // the good corpus — the operator was left with neither the old data nor the new. There
            // was no rollback because there was nothing left to roll back to.
            //
            // Retiring the old run moves to the commit point (PatchRun -> complete), where flipping
            // one run document's status is a single-document write and therefore atomic. That one
            // write is the transaction: before it readers see the old corpus whole, after it they
            // see the new corpus whole, and no reader ever observes a partial crawl.
            //
            // Reclaim abandoned staging from earlier crawls that died in this slot. They never
            // published, so nothing ever read them and nothing is lost.
            var abandoned = await _repo
                .ListUncommittedRunsForSlotAsync(ownerUserId, crawlType, seedKey, ct)
                .ConfigureAwait(false);

            foreach (var stale in abandoned)
            {
                if (!_rag.IsEnabled)
                {
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        "Geek-Crawler-Rag is disabled — abandoned staging vectors cannot be purged, "
                        + "so no new crawl was started.");
                }

                if (!await _rag.DeleteRunIndexAsync(stale.Id, ct).ConfigureAwait(false))
                {
                    return StatusCode(
                        StatusCodes.Status502BadGateway,
                        $"Vector purge failed for abandoned staging run {stale.Id:D} — no new crawl "
                        + "was started.");
                }

                await _repo.ClearRunCrawlDataAsync(stale.Id, ct).ConfigureAwait(false);
                await _repo.DeleteRunAsync(stale.Id, ct).ConfigureAwait(false);
            }

            // Always a fresh run. The published run for this slot, if any, stays readable until the
            // new one commits.
            var run = await _repo.CreateRunAsync(
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

        var committing = string.Equals(request.Status, "complete", StringComparison.OrdinalIgnoreCase);

        try
        {
            // Identify what this slot publishes today BEFORE committing, so the outgoing run is known
            // even though the commit itself is what makes the new one visible.
            GeekCrawlerRunDto? outgoing = null;
            if (committing)
            {
                var staging = await _repo.GetRunAsync(runId, ct).ConfigureAwait(false);
                if (staging?.SeedKey is { Length: > 0 } seedKey)
                {
                    outgoing = await _repo.GetRunForSlotAsync(
                        staging.OwnerUserId, staging.CrawlType, seedKey, publishedOnly: true, ct)
                        .ConfigureAwait(false);

                    if (outgoing is not null && outgoing.Id == runId)
                        outgoing = null;
                }
            }

            // The commit. Flipping one run document's status is a single-document write, so it is
            // atomic without any distributed protocol: readers resolving this slot see the old run
            // whole before it and the new run whole after it, and never a crawl in progress.
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

            // Retire the superseded run only after the new one is published, and only if it still
            // is not the published one. Failing here costs disk, never correctness: two complete runs
            // in a slot resolve to the newer by CreatedAtUtc, which is the one just committed.
            if (outgoing is not null)
            {
                if (_rag.IsEnabled
                    && await _rag.DeleteRunIndexAsync(outgoing.Id, ct).ConfigureAwait(false))
                {
                    await _repo.DeleteRunAsync(outgoing.Id, ct).ConfigureAwait(false);
                }
                else
                {
                    // Deleting pages whose vectors survive would leave the index pointing at rows
                    // that no longer exist. Keep both rather than create that inconsistency.
                    _logger.LogWarning(
                        "Superseded crawl run {OutgoingRunId} was left in place: vectors could not be "
                        + "purged. The slot publishes {RunId}; the old run is now dead storage.",
                        outgoing.Id,
                        runId);
                }
            }

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
