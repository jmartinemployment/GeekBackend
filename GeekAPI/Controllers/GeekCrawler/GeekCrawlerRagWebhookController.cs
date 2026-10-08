using System.Net;
using System.Net.Http;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.GeekCrawler;

/// <summary>
/// Internal webhook from Geek-Crawler-Rag: index status → SignalR (no UI polling).
/// Auth: <c>X-API-Key: GEEK_BACKEND_API_KEY</c>.
/// </summary>
[ApiController]
[Route("api/geek-crawler/internal/rag")]
public sealed class GeekCrawlerRagWebhookController : ControllerBase
{
    private readonly HttpGeekCrawlerRepository _repo;
    private readonly GeekCrawlerProgressNotifier _notifier;
    private readonly ILogger<GeekCrawlerRagWebhookController> _logger;

    public GeekCrawlerRagWebhookController(
        HttpGeekCrawlerRepository repo,
        GeekCrawlerProgressNotifier notifier,
        ILogger<GeekCrawlerRagWebhookController> logger)
    {
        _repo = repo;
        _notifier = notifier;
        _logger = logger;
    }

    [HttpPost("index-status")]
    public async Task<IActionResult> IndexStatus(
        [FromBody] RagIndexStatusWebhookRequest? body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.RunId))
            return BadRequest("runId is required");

        if (!Guid.TryParse(body.RunId, out var runId))
            return BadRequest("runId must be a GUID");

        var run = await _repo.GetRunAsync(runId, ct).ConfigureAwait(false);

        // There is nothing to record a status onto, and saying so is the point. The $set has no
        // upsert, so this frame matched no document, raised nothing, and was answered 202 -- the
        // sender was told its numbers had landed while they were dropped on the floor. A run
        // legitimately disappears (the crawler purges a failed run from GeekAPI), and the Library
        // holding a job for it needs to learn that rather than be reassured.
        if (run is null)
        {
            _logger.LogWarning(
                "RAG index status for unknown run {RunId} — nothing to persist it onto. The run was "
                + "purged or never ingested; the Library still holds an index job for it.",
                runId);
            return NotFound($"No crawl run {runId:D}.");
        }

        var ownerUserId = run.OwnerUserId ?? "";

        // Duplicate/replay guard, and only that.
        //
        // It compares FinishedAtUtc, which is null until a run reaches a terminal state, so for a
        // mid-run progress frame it reduces to "RagIndexedAtUtc is null" -- true for the whole run --
        // and every such frame persists in arrival order with no comparison made. That is sound only
        // because nothing sends these concurrently: the indexer runs one job at a time and awaits
        // each `notify()` before the next line, and the heartbeat task renews the Mongo lease without
        // emitting frames, so a second frame for one run is not sent until the first has returned and
        // cannot overtake it. `status_store.save` rejects a stale write on the sender's side as well.
        //
        // Do not restate this as out-of-order protection. It is not one for mid-run frames, and an
        // ordering key (a sender-stamped sentAtUtc) is only worth adding if the sender ever emits
        // frames in parallel -- at which point this comment is the thing that says so.
        if (run.RagIndexedAtUtc is null || body.FinishedAtUtc > run.RagIndexedAtUtc)
        {
            try
            {
                await _repo.UpdateRagIndexStatusAsync(
                    runId,
                    new PatchRagIndexStatusCommand(
                        RagState: body.State,
                        RagChunksUpserted: body.ChunksUpserted,
                        RagPagesEnglish: body.PagesEnglish,
                        RagPagesSkippedUnusable: body.PagesSkippedUnusable,
                        RagIndexedAtUtc: body.FinishedAtUtc),
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A swallowed persist failure returning 202 is plans/rules.md 3a verbatim: the one
                // failure mode that loses these numbers was the one the sender could not learn
                // about. It logs at Error and answers with a status, so notify()'s own error branch
                // fires. Cancellation is excluded -- a disconnected client is not a failed write.
                _logger.LogError(ex, "Failed to persist RAG index status for {RunId}", runId);

                // A 404 out of GeekRepository means the run went between the read above and this
                // patch; anything else is the hop itself failing. Neither is this run's fault and
                // neither may be reported as accepted.
                var notFound = ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound };
                return notFound
                    ? NotFound($"Crawl run {runId:D} no longer exists; status was not recorded.")
                    : StatusCode(
                        StatusCodes.Status502BadGateway,
                        $"Could not record RAG index status for {runId:D}. Nothing was written.");
            }
        }

        var payload = new
        {
            eventType = "rag_index",
            runId = runId.ToString("D"),
            state = body.State ?? "unknown",
            crawlType = body.CrawlType ?? run.CrawlType,
            mongoPageCount = body.MongoPageCount,
            pagesSeen = body.PagesSeen,
            pagesEnglish = body.PagesEnglish,
            pagesSkippedLang = body.PagesSkippedLang,
            pagesSkippedEmpty = body.PagesSkippedEmpty,
            pagesSkippedUnusable = body.PagesSkippedUnusable,
            chunksUpserted = body.ChunksUpserted,
            error = body.Error,
            startedAtUtc = body.StartedAtUtc,
            finishedAtUtc = body.FinishedAtUtc,
            attempt = body.Attempt,
            trigger = body.Trigger,
            embeddingRateLimitRetries = body.EmbeddingRateLimitRetries,
            embeddingWaitSeconds = body.EmbeddingWaitSeconds,
        };

        try
        {
            await _notifier.PushRagIndexAsync(payload, runId, ownerUserId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to SignalR-push RAG index status for {RunId}", runId);
            return StatusCode(StatusCodes.Status502BadGateway, "SignalR push failed");
        }

        return Accepted(payload);
    }
}

public sealed class RagIndexStatusWebhookRequest
{
    public string? RunId { get; set; }
    public string? State { get; set; }
    public string? CrawlType { get; set; }
    public int? MongoPageCount { get; set; }
    public int PagesSeen { get; set; }
    public int PagesEnglish { get; set; }
    public int PagesSkippedLang { get; set; }
    public int PagesSkippedEmpty { get; set; }

    /// <summary>
    /// Pages the Library refused as not citable: a 4xx/5xx body, robots-denied, a non-English
    /// locale path, or a recorded fetch failure. Sent since the reject gate shipped and bound
    /// here since 2026-09-29 -- before that GeekAPI dropped it on the floor while binding both
    /// benign siblings, so a run whose corpus was gutted by error pages was indistinguishable
    /// from one that indexed cleanly: pagesSeen 500 / pagesEnglish 40 with skippedLang and
    /// skippedEmpty both 0, and no field anywhere accounting for the other 460.
    /// </summary>
    public int PagesSkippedUnusable { get; set; }

    public int ChunksUpserted { get; set; }

    /// <summary>
    /// Chunks the Library did not write because an earlier page of the same run carried the
    /// exact text. Bound so it is not discarded on arrival; not persisted to crawl_runs.
    /// </summary>
    public int ChunksSkippedRepeat { get; set; }
    public int ChunksSkippedStub { get; set; }

    public string? Error { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }

    /// <summary>
    /// Job-level attempt number. Bound so it can be logged and surfaced, NOT used by the
    /// out-of-order guard below, which still keys on FinishedAtUtc alone -- see the comment
    /// there. Binding it does not change that decision.
    /// </summary>
    public int Attempt { get; set; }

    /// <summary>What started the run: the scheduler, or a manual trigger.</summary>
    public string? Trigger { get; set; }

    /// <summary>Embedding throttle telemetry, for cost and headroom questions.</summary>
    public int EmbeddingRateLimitRetries { get; set; }

    public double EmbeddingWaitSeconds { get; set; }

    public string? EventType { get; set; }
}
