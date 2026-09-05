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
            chunksUpserted = body.ChunksUpserted,
            error = body.Error,
            startedAtUtc = body.StartedAtUtc,
            finishedAtUtc = body.FinishedAtUtc,
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
    public int ChunksUpserted { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string? EventType { get; set; }
}
