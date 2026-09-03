using GeekRepository.Auth;
using GeekRepository.Data.Entities.GeekCrawler;
using GeekRepository.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.GeekCrawler;

[ApiController]
[Route("repo/geek-crawler/runs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GeekCrawlerRunsController : ControllerBase
{
    private readonly IMongoGeekCrawlerService _mongo;

    public GeekCrawlerRunsController(IMongoGeekCrawlerService mongo) => _mongo = mongo;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GeekCrawlerRun>> GetById(Guid id, CancellationToken ct)
    {
        var row = await _mongo.GetRunByIdAsync(id, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpGet("for-user")]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerRun>>> ListForUser(
        [FromQuery] string ownerUserId,
        [FromQuery] string? crawlType = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return BadRequest("ownerUserId is required");

        limit = Math.Clamp(limit, 1, 200);
        var rows = await _mongo.ListRunsByUserAsync(ownerUserId, crawlType, limit, ct);
        return Ok(rows);
    }

    [HttpGet("for-slot")]
    public async Task<ActionResult<GeekCrawlerRun>> GetForSlot(
        [FromQuery] string ownerUserId,
        [FromQuery] string crawlType,
        [FromQuery] string seedKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return BadRequest("ownerUserId is required");
        if (string.IsNullOrWhiteSpace(crawlType))
            return BadRequest("crawlType is required");
        if (string.IsNullOrWhiteSpace(seedKey))
            return BadRequest("seedKey is required");

        var row = await _mongo.GetRunForSlotAsync(ownerUserId, crawlType.Trim(), seedKey, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpGet("latest")]
    public async Task<ActionResult<GeekCrawlerRun>> GetLatest(
        [FromQuery] string ownerUserId,
        [FromQuery] string crawlType,
        [FromQuery] string seedsJson,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return BadRequest("ownerUserId is required");
        if (string.IsNullOrWhiteSpace(crawlType))
            return BadRequest("crawlType is required");
        if (string.IsNullOrWhiteSpace(seedsJson))
            return BadRequest("seedsJson is required");

        var row = await _mongo.GetLatestRunAsync(ownerUserId, crawlType.Trim(), seedsJson, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpGet("by-status/{status}")]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerRun>>> ListByStatus(
        string status,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(status))
            return BadRequest("status is required");

        limit = Math.Clamp(limit, 1, 200);
        var rows = await _mongo.ListRunsByStatusAsync(status, limit, ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<GeekCrawlerRun>> Create(
        [FromBody] CreateGeekCrawlerRunCommand command,
        CancellationToken ct)
    {
        if (command is null
            || string.IsNullOrWhiteSpace(command.OwnerUserId)
            || string.IsNullOrWhiteSpace(command.CrawlType))
            return BadRequest("ownerUserId and crawlType are required");

        var row = new GeekCrawlerRun
        {
            Id = Guid.NewGuid(),
            OwnerUserId = command.OwnerUserId.Trim(),
            CrawlType = command.CrawlType.Trim(),
            Status = "pending",
            SeedUrlsJson = string.IsNullOrWhiteSpace(command.SeedUrlsJson) ? "[]" : command.SeedUrlsJson,
            SeedKey = string.IsNullOrWhiteSpace(command.SeedKey) ? null : command.SeedKey.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        var created = await _mongo.CreateRunAsync(row, ct);
        return Ok(created);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GeekCrawlerRun>> Patch(
        Guid id,
        [FromBody] PatchGeekCrawlerRunCommand command,
        CancellationToken ct)
    {
        try
        {
            GeekCrawlerRun? row = null;
            await _mongo.UpdateRunAsync(id, r =>
            {
                if (!string.IsNullOrWhiteSpace(command.Status))
                    r.Status = command.Status.Trim();
                if (command.HostProgressJson is not null)
                    r.HostProgressJson = command.HostProgressJson;
                if (command.ErrorSummary is not null)
                    r.ErrorSummary = command.ErrorSummary;
                if (command.StartedAtUtc is not null)
                    r.StartedAtUtc = command.StartedAtUtc;
                if (command.CompletedAtUtc is not null)
                    r.CompletedAtUtc = command.CompletedAtUtc;
                row = r;
            }, ct);
            return Ok(row);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}/crawl-data")]
    public async Task<IActionResult> ClearCrawlData(Guid id, CancellationToken ct)
    {
        var run = await _mongo.GetRunByIdAsync(id, ct);
        if (run is null)
            return NotFound();

        await _mongo.DeleteRunCrawlDataAsync(id, ct);
        return NoContent();
    }

    public record CreateGeekCrawlerRunCommand(
        string OwnerUserId,
        string CrawlType,
        string? SeedUrlsJson,
        string? SeedKey = null);

    public record PatchGeekCrawlerRunCommand(
        string? Status = null,
        string? HostProgressJson = null,
        string? ErrorSummary = null,
        DateTimeOffset? StartedAtUtc = null,
        DateTimeOffset? CompletedAtUtc = null);
}
