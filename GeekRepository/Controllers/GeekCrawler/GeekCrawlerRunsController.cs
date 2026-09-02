using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.GeekCrawler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.GeekCrawler;

[ApiController]
[Route("repo/geek-crawler/runs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GeekCrawlerRunsController : ControllerBase
{
    private readonly GeekCrawlerDbContext _db;

    public GeekCrawlerRunsController(GeekCrawlerDbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GeekCrawlerRun>> GetById(Guid id, CancellationToken ct)
    {
        var row = await _db.GeekCrawlerRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
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
        var query = _db.GeekCrawlerRuns.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId);

        if (!string.IsNullOrWhiteSpace(crawlType))
        {
            var type = crawlType.Trim();
            query = query.Where(r => r.CrawlType == type);
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);

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

        var row = await _db.GeekCrawlerRuns.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId
                        && r.CrawlType == crawlType.Trim()
                        && r.SeedKey == seedKey)
            .FirstOrDefaultAsync(ct);

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

        var type = crawlType.Trim();
        var row = await _db.GeekCrawlerRuns.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.CrawlType == type && r.SeedUrlsJson == seedsJson)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

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
        var normalized = status.Trim().ToLowerInvariant();
        var rows = await _db.GeekCrawlerRuns.AsNoTracking()
            .Where(r => r.Status.ToLower() == normalized)
            .OrderBy(r => r.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);

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

        _db.GeekCrawlerRuns.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GeekCrawlerRun>> Patch(
        Guid id,
        [FromBody] PatchGeekCrawlerRunCommand command,
        CancellationToken ct)
    {
        var row = await _db.GeekCrawlerRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(command.Status))
            row.Status = command.Status.Trim();
        if (command.HostProgressJson is not null)
            row.HostProgressJson = command.HostProgressJson;
        if (command.ErrorSummary is not null)
            row.ErrorSummary = command.ErrorSummary;
        if (command.StartedAtUtc is not null)
            row.StartedAtUtc = command.StartedAtUtc;
        if (command.CompletedAtUtc is not null)
            row.CompletedAtUtc = command.CompletedAtUtc;

        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpDelete("{id:guid}/crawl-data")]
    public async Task<IActionResult> ClearCrawlData(Guid id, CancellationToken ct)
    {
        var exists = await _db.GeekCrawlerRuns.AsNoTracking()
            .AnyAsync(r => r.Id == id, ct);
        if (!exists)
            return NotFound();

        await _db.GeekCrawlerLinks.Where(l => l.RunId == id).ExecuteDeleteAsync(ct);
        await _db.GeekCrawlerPages.Where(p => p.RunId == id).ExecuteDeleteAsync(ct);
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
