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

        try
        {
            var run = await _repo.CreateRunAsync(
                new CreateGeekCrawlerRunCommand(
                    ownerUserId,
                    request.CrawlType.Trim(),
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
                    CompletedAtUtc: request.CompletedAtUtc),
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

        var items = request.Pages.Select(p => new CreateGeekCrawlerPageItemCommand(
            p.Origin ?? "",
            p.Url ?? "",
            p.FinalUrl,
            p.StatusCode,
            p.RobotsAllowed,
            p.Html,
            p.FailureReason)).ToList();

        try
        {
            var result = await _repo.CreatePagesBatchAsync(
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

            return Ok(result);
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
        DateTimeOffset? CompletedAtUtc = null);

    public record IngestPagesBatchRequest(IReadOnlyList<IngestPageItem>? Pages);

    public record IngestPageItem(
        string? Origin,
        string? Url,
        string? FinalUrl,
        int StatusCode,
        bool RobotsAllowed,
        string? Html,
        string? FailureReason = null);

    public record IngestLinksBatchRequest(IReadOnlyList<IngestLinkItem>? Links);

    public record IngestLinkItem(
        Guid PageId,
        string? FromUrl,
        string? LinkUrl,
        bool IsSameOrigin);
}
