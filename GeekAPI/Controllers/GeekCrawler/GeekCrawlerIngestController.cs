using System.Globalization;
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

    /// <summary>
    /// Pages assumed for a slot that has never been crawled, per crawl type — these mirror the page
    /// budgets in Geek-Crawler-v2's crawl-profile.ts. A first crawl has nothing to measure, so the
    /// type's own ceiling is the only honest estimate. Using one number for every type would refuse
    /// small competitor crawls on the strength of a project-site budget.
    /// </summary>
    private static long FirstCrawlPageEstimate(string crawlType) => crawlType switch
    {
        CrawlTypes.Partner => 2_500,
        CrawlTypes.ProjectSite => 2_500,
        CrawlTypes.Competitors => 150,
        CrawlTypes.Local => 100,
        _ => 2_500,
    };

    /// <summary>
    /// Links per page in the measured corpus: 9,850,985 links against 86,165 pages. Used only for a
    /// first crawl; a re-crawl counts the previous crawl's actual links.
    /// </summary>
    private const long DefaultLinksPerPage = 114;

    /// <summary>
    /// Fraction of free disk a single staging crawl may consume. Atomic publish holds the outgoing
    /// and incoming copies of a site at once, so the headroom check is what keeps "never purge before
    /// the replacement exists" from becoming "fill the disk and lose both".
    /// </summary>
    private const double MaxFreeSpaceFraction = 0.5;

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

        // One unusable URL does not spoil the list; refuse only when none can be crawled.
        var admission = GeekCrawlerSeedNormalizer.AdmitSeeds(request.Seeds);
        var seeds = admission.Accepted;
        if (seeds.Count == 0)
        {
            return BadRequest(new
            {
                error = "No usable seed URLs.",
                rejected = admission.Rejected,
            });
        }

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
            // FULL STOP gate, before any crawl starts anywhere.
            //
            // A run that ended in failure while still holding pages is a discard that did not
            // complete, which means Qdrant and the corpus may disagree. That is an unresolved
            // integrity break, not a tidy-up task, and it is not confined to one site: an index the
            // system cannot delete from is unhealthy for every crawl, so this gate is global to the
            // owner rather than per slot.
            //
            // The discard is RETRIED here rather than merely detected, so the moment Qdrant is
            // healthy again the next crawl clears the block by itself. It only halts if the delete
            // still will not succeed.
            if (!_rag.IsEnabled)
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    "Geek-Crawler-Rag is disabled, so vectors cannot be deleted. No crawl was "
                    + "started.");
            }

            var unresolved = await _repo.ListFailedRunsHoldingDataAsync(ownerUserId, ct)
                .ConfigureAwait(false);

            foreach (var stuck in unresolved)
            {
                if (!await _rag.DeleteRunIndexAsync(stuck.Id, ct).ConfigureAwait(false))
                {
                    _logger.LogError(
                        "Crawling is stopped: vectors for failed run {StuckRunId} still cannot be "
                        + "deleted, so its pages cannot be discarded.",
                        stuck.Id);

                    return StatusCode(
                        StatusCodes.Status502BadGateway,
                        $"Crawling is stopped. Failed run {stuck.Id:D} is still holding pages "
                        + "because its vectors cannot be deleted from Qdrant. Resolve the Qdrant "
                        + "deletion before starting any crawl — retrying this request will clear "
                        + "the block once it succeeds.");
                }

                await _repo.ClearRunCrawlDataAsync(stuck.Id, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Cleared previously stuck discard for failed run {StuckRunId}.",
                    stuck.Id);
            }

            // Reclaim abandoned staging from earlier crawls that died in this slot. They never
            // published, so nothing ever read them and nothing is lost.
            var abandoned = await _repo
                .ListUncommittedRunsForSlotAsync(ownerUserId, crawlType, seedKey, ct)
                .ConfigureAwait(false);

            var published = await _repo
                .GetRunForSlotAsync(ownerUserId, crawlType, seedKey, publishedOnly: true, ct)
                .ConfigureAwait(false);

            foreach (var stale in abandoned)
            {
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

            // Capacity preflight. Staging a second copy is what makes the publish atomic, and it is
            // also what can fill the disk: a 50,000-page site held twice is not a rounding error.
            // Refusing here costs the operator a message; discovering it at page 40,000 costs the
            // crawl AND leaves the host wedged for everything else sharing the volume.
            //
            // Measured against what the previous crawl of THIS site produced, because that is the
            // only honest predictor of what the next one will.
            var capacityError = await CheckCapacityAsync(published?.Id, crawlType, ct).ConfigureAwait(false);
            if (capacityError is not null)
                return StatusCode(StatusCodes.Status507InsufficientStorage, capacityError);

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

            // Rejected seeds travel with the run so the crawler can report what it will not fetch,
            // rather than the operator inferring it from a page count that came up short.
            return Ok(new
            {
                run = snapshot,
                seedsAccepted = seeds.Count,
                rejected = admission.Rejected,
            });
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
        var aborting = string.Equals(request.Status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Status, "cancelled", StringComparison.OrdinalIgnoreCase);

        try
        {
            // A crawl that collected nothing may not publish. The in-process path has always
            // enforced this (GeekCrawlerService + DescribeZeroHtmlFailure); the external path never
            // did, and atomic publish made that gap dangerous: an empty run reaching "complete"
            // would retire the previously published corpus and replace it with nothing.
            //
            // Ingest already rejects pages carrying neither Html nor Markdown, so a page count above
            // zero means at least one usable page was stored.
            if (committing)
            {
                var activity = await _repo.GetPageActivityAsync(runId, ct).ConfigureAwait(false);
                if (activity is null or { PageCount: 0 })
                {
                    const string reason =
                        "Crawl reported complete with no usable pages — every fetch failed, was "
                        + "disallowed by robots, or returned empty content. Nothing was published.";

                    await _repo.PatchRunAsync(
                        runId,
                        new PatchGeekCrawlerRunCommand(
                            Status: "failed",
                            ErrorSummary: string.IsNullOrWhiteSpace(request.ErrorSummary)
                                ? reason
                                : reason + " Crawler reported: " + request.ErrorSummary,
                            CompletedAtUtc: DateTimeOffset.UtcNow,
                            CrawlReportJson: BuildReportJson(request.Report, 0, GeekCrawlerRunOutcome.Discarded)),
                        ct).ConfigureAwait(false);

                    return Conflict(reason);
                }
            }

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
            // Reporting phase. What the crawl COLLECTED is measured here, before any discard, and
            // stays true regardless of what happens to the data afterwards.
            //
            // What is RETAINED cannot be known yet on an abort: the discard has not run, and it can
            // fail (vectors unpurgeable, so pages are kept). So a committing run reports now — it
            // retains everything it collected — and an aborting run reports after the discard, when
            // the answer exists.
            var pagesCollected = committing || aborting
                ? (await _repo.GetPageActivityAsync(runId, ct).ConfigureAwait(false))?.PageCount ?? 0
                : 0;

            var reportJson = committing
                ? BuildReportJson(request.Report, pagesCollected, GeekCrawlerRunOutcome.Published)
                : null;

            var run = await _repo.PatchRunAsync(
                runId,
                new PatchGeekCrawlerRunCommand(
                    Status: request.Status,
                    ErrorSummary: request.ErrorSummary,
                    HostProgressJson: request.HostProgressJson,
                    StartedAtUtc: request.StartedAtUtc,
                    CompletedAtUtc: request.CompletedAtUtc,
                    MarkdownReadyAt: request.MarkdownReadyAt,
                    ClearMarkdownReadyAt: request.ClearMarkdownReadyAt,
                    CrawlReportJson: reportJson),
                ct).ConfigureAwait(false);

            // Retire the superseded run only after the new one is published, and only if it still
            // is not the published one. Failing here costs disk, never correctness: two complete runs
            // in a slot resolve to the newer by CreatedAtUtc, which is the one just committed.
            if (outgoing is not null)
            {
                if (!_rag.IsEnabled)
                {
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        $"Run {runId:D} is published, but Geek-Crawler-Rag is disabled so the "
                        + $"superseded run {outgoing.Id:D} could not have its vectors purged. "
                        + "Crawling is stopped until that is resolved.");
                }

                if (!await _rag.DeleteRunIndexAsync(outgoing.Id, ct).ConfigureAwait(false))
                {
                    // Full stop. The previous shape logged a warning and returned 200, which made
                    // the guard advisory: the one moment index/corpus consistency actually needed
                    // enforcing was the moment the code declined to enforce it. "Costs disk, never
                    // correctness" was a rationalisation, and it was a fallback.
                    _logger.LogError(
                        "Vector purge failed for superseded run {OutgoingRunId} after {RunId} "
                        + "published. Crawling is stopped until Qdrant deletion succeeds.",
                        outgoing.Id,
                        runId);

                    return StatusCode(
                        StatusCodes.Status502BadGateway,
                        $"Run {runId:D} is published, but vectors for the superseded run "
                        + $"{outgoing.Id:D} could not be purged. Crawling is stopped until Qdrant "
                        + "deletion succeeds for that run.");
                }

                await _repo.DeleteRunAsync(outgoing.Id, ct).ConfigureAwait(false);
            }

            // Abort. A crawl that failed or was cancelled never published, so the pages it managed to
            // collect are a prefix nothing has read and nothing may read. Discarding them now is what
            // makes the result binary: the operator ends with the previously published corpus intact
            // and no partial second copy, rather than dead pages waiting for the next crawl to sweep.
            //
            // The run document survives on purpose, carrying its status and ErrorSummary. It stays
            // uncommitted, so it is invisible to every reader, and it costs a few hundred bytes
            // against the hundreds of MB its pages would have.
            if (aborting)
            {
                if (!_rag.IsEnabled)
                {
                    return StatusCode(
                        StatusCodes.Status503ServiceUnavailable,
                        $"Run {runId:D} is marked {run.Status}, but Geek-Crawler-Rag is disabled so "
                        + "its pages could not be discarded. Crawling is stopped until that is "
                        + "resolved.");
                }

                if (!await _rag.DeleteRunIndexAsync(run.Id, ct).ConfigureAwait(false))
                {
                    // Full stop. The previous shape logged a warning and returned 200, which made
                    // the guard advisory: the one moment index/corpus consistency actually needed
                    // enforcing was the moment the code declined to enforce it. Clearing pages whose
                    // vectors survive orphans the index; leaving them reports an abort that did not
                    // happen. Neither is something to return success for.
                    _logger.LogError(
                        "Vector purge failed for aborted run {RunId}; its pages were not discarded. "
                        + "Crawling is stopped until Qdrant deletion succeeds.",
                        run.Id);

                    return StatusCode(
                        StatusCodes.Status502BadGateway,
                        $"Run {runId:D} is marked {run.Status}, but its vectors could not be purged "
                        + "so its pages were not discarded. Crawling is stopped until Qdrant "
                        + "deletion succeeds for that run.");
                }

                await _repo.ClearRunCrawlDataAsync(run.Id, ct).ConfigureAwait(false);

                // Written only once the discard has happened, so Discarded is a fact rather than an
                // intention.
                run = await _repo.PatchRunAsync(
                    run.Id,
                    new PatchGeekCrawlerRunCommand(
                        CrawlReportJson: BuildReportJson(
                            request.Report,
                            pagesCollected,
                            GeekCrawlerRunOutcome.Discarded)),
                    ct).ConfigureAwait(false);
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

            // Rejections are reported, not just counted. They used to exist only in the HTTP
            // response to the crawler, so unless the crawler surfaced them they vanished — pages
            // silently dropped while the run went on to complete with a smaller corpus and no
            // record of why. That is the "page skipped" middle state that hid a total extraction
            // outage behind thirteen drafts of filler.
            if (rejectedCount > 0)
            {
                _logger.LogWarning(
                    "Crawl run {RunId} rejected {RejectedCount} of {BatchCount} pages "
                    + "(robotsDisallowed={RobotsDisallowed}, failureReason={FailureReason}, "
                    + "blankContent={BlankContent}).",
                    runId,
                    rejectedCount,
                    request.Pages.Count,
                    robotsDisallowedCount,
                    failureReasonCount,
                    blankContentCount);
            }

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
                        // Carried live so a crawl shedding most of its pages is visible while it
                        // runs, not discovered afterwards from a page count that looks low.
                        rejectedInBatch = rejectedCount,
                        rejectedReasonCounts = new
                        {
                            robotsDisallowed = robotsDisallowedCount,
                            failureReason = failureReasonCount,
                            blankContent = blankContentCount,
                        },
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

    /// <summary>
    /// Assembles the run's completion report.
    ///
    /// The per-page taxonomy is the CRAWLER's — it is the only component that saw each fetch, and it
    /// already classifies every rejection (robots, locale, challenge, request failed, extract empty)
    /// with sanitized sample URLs. Until now it wrote that to local disk and never sent it, so
    /// GeekAPI re-derived a cruder three-bucket count from whatever arrived in a batch and threw
    /// that away too.
    ///
    /// Stored counts come from the store, because they are the ones that must be true: a crawler
    /// claiming 2,000 pages does not make 2,000 pages exist.
    /// </summary>
    private static string BuildReportJson(
        GeekCrawlerRunReport? crawlerReport,
        int pagesCollected,
        GeekCrawlerRunOutcome outcome)
    {
        var report = (crawlerReport ?? new GeekCrawlerRunReport()) with
        {
            PagesCollected = pagesCollected,
            Outcome = outcome,
        };

        return report.ToJson();
    }

    /// <summary>
    /// A finished crawl's report: stored, excluded by policy, failed by cause, with sample URLs.
    /// Absent until the crawl reaches a terminal state.
    /// </summary>
    [HttpGet("runs/{runId:guid}/report")]
    public async Task<IActionResult> GetRunReport(Guid runId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (!await OwnsRunAsync(runId, ct).ConfigureAwait(false)) return NotFound();

        try
        {
            var run = await _repo.GetRunAsync(runId, ct).ConfigureAwait(false);
            if (run is null) return NotFound();

            var report = GeekCrawlerRunReport.FromJson(run.CrawlReportJson);
            if (report is null)
            {
                return NotFound(
                    $"Crawl {runId:D} has no report — status is '{run.Status}'. A report is written "
                    + "when a crawl completes or aborts.");
            }

            return Ok(new
            {
                runId,
                status = run.Status,
                errorSummary = run.ErrorSummary,
                report.PagesCollected,
                report.Outcome,
                report.LinksStored,
                report.ExcludedByPolicy,
                report.Failed,
                report.StatusCounts,
                report.Samples,
                totalFailed = report.TotalFailed,
                totalExcluded = report.TotalExcluded,
                failureRate = Math.Round(report.FailureRate, 4),
            });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// Crawls that did not publish, newest first, with the reason each one gave.
    ///
    /// A failed crawl records its ErrorSummary, but until now nothing read it back: the detail was
    /// captured and unreachable, which is the same as not capturing it. A failure you cannot read is
    /// a failure you cannot fix.
    /// </summary>
    [HttpGet("failures")]
    public async Task<IActionResult> ListFailures(
        [FromQuery] int limit,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var ownerUserId = _user.UserId.ToString("D");
        var capped = limit <= 0 ? 50 : Math.Clamp(limit, 1, 200);

        try
        {
            var runs = await _repo.ListRunsForUserAsync(ownerUserId, crawlType: null, capped, ct)
                .ConfigureAwait(false);

            var failures = runs
                .Where(r => !string.Equals(r.Status, "complete", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.CreatedAtUtc)
                .Select(r => new
                {
                    runId = r.Id,
                    crawlType = r.CrawlType,
                    status = r.Status,
                    seedUrls = GeekCrawlerService.ToSnapshot(r).SeedUrls,
                    // The reason, verbatim. Null means the crawl died without reporting one —
                    // killed process, closed laptop — which is itself the diagnosis.
                    errorSummary = r.ErrorSummary,
                    // Present once the crawl reached a terminal state. Null on a run that was
                    // killed outright, which is why errorSummary is not the only field here.
                    report = GeekCrawlerRunReport.FromJson(r.CrawlReportJson) is { } rep
                        ? new
                        {
                            rep.PagesCollected,
                            rep.Outcome,
                            rep.ExcludedByPolicy,
                            rep.Failed,
                            totalFailed = rep.TotalFailed,
                            failureRate = Math.Round(rep.FailureRate, 4),
                        }
                        : null,
                    createdAtUtc = r.CreatedAtUtc,
                    startedAtUtc = r.StartedAtUtc,
                    completedAtUtc = r.CompletedAtUtc,
                })
                .ToList();

            return Ok(failures);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// Returns a refusal message when the incoming crawl cannot be held alongside what is already
    /// published, or null when it fits. Headroom that cannot be measured is refused rather than
    /// assumed — an unmeasured disk is exactly the one that fills.
    /// </summary>
    private async Task<string?> CheckCapacityAsync(
        Guid? publishedRunId,
        string crawlType,
        CancellationToken ct)
    {
        var headroom = await _repo.GetStorageHeadroomAsync(ct).ConfigureAwait(false);
        if (headroom is null)
        {
            return "Storage headroom could not be read from the crawl store, so it cannot be "
                + "confirmed that this crawl fits alongside the copy already published. No crawl "
                + "was started.";
        }

        // No pages stored anywhere yet: nothing has a measured size, so there is nothing to check
        // against. The first crawl into an empty corpus is also the one least able to fill a disk.
        if (headroom.AvgPageBytes is not { } avgPageBytes)
            return null;

        // A re-crawl is measured against what the previous crawl of THIS site produced — the only
        // honest predictor available. It assumes the site has not grown since, which is the known
        // weakness of this estimate and the reason the ceiling is 50% of free space, not 90%.
        var estimatedPages = FirstCrawlPageEstimate(crawlType);
        if (publishedRunId is { } runId)
        {
            var activity = await _repo.GetPageActivityAsync(runId, ct).ConfigureAwait(false);
            if (activity is { PageCount: > 0 })
                estimatedPages = activity.PageCount;
        }

        var estimatedBytes = estimatedPages * avgPageBytes;
        if (headroom.AvgLinkBytes is { } avgLinkBytes)
            estimatedBytes += estimatedPages * DefaultLinksPerPage * avgLinkBytes;

        var budget = (long)(headroom.FreeBytes * MaxFreeSpaceFraction);
        if (estimatedBytes <= budget)
            return null;

        return string.Format(
            CultureInfo.InvariantCulture,
            "Insufficient storage: this crawl is estimated at {0:N1} GB ({1:N0} pages x {2:N0} KB, "
            + "plus links), but only {3:N1} GB is free and a single crawl may use at most {4:P0} of "
            + "it. The copy already published is kept and no crawl was started. Free space on the "
            + "crawl store, or narrow this crawl's scope.",
            estimatedBytes / 1024d / 1024d / 1024d,
            estimatedPages,
            avgPageBytes / 1024d,
            headroom.FreeBytes / 1024d / 1024d / 1024d,
            MaxFreeSpaceFraction);
    }

    public record IngestCreateRunRequest(string CrawlType, string[]? Seeds);

    public record IngestPatchRunRequest(
        string? Status = null,
        string? ErrorSummary = null,
        string? HostProgressJson = null,
        DateTimeOffset? StartedAtUtc = null,
        DateTimeOffset? CompletedAtUtc = null,
        DateTimeOffset? MarkdownReadyAt = null,
        bool ClearMarkdownReadyAt = false,
        GeekCrawlerRunReport? Report = null);

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
