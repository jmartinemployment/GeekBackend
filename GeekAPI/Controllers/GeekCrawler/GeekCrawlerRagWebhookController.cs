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
        var ownerUserId = run?.OwnerUserId ?? "";

        // Out-of-order/duplicate guard: the indexer retries at the job level (status.attempt), so
        // more than one webhook call for the same runId is possible even without network retries.
        // A stale delivery must never overwrite a newer, more-authoritative state.
        if (run is null || run.RagIndexedAtUtc is null || body.FinishedAtUtc > run.RagIndexedAtUtc)
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist RAG index status for {RunId}", runId);
            }
        }

        var payload = new
        {
            eventType = "rag_index",
            runId = runId.ToString("D"),
            state = body.State ?? "unknown",
            crawlType = body.CrawlType ?? run?.CrawlType,
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
