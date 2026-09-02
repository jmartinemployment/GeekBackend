using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.GeekCrawler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.GeekCrawler;

[ApiController]
[Route("repo/geek-crawler/schedules")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GeekCrawlerSchedulesController : ControllerBase
{
    private readonly GeekCrawlerDbContext _db;

    public GeekCrawlerSchedulesController(GeekCrawlerDbContext db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GeekCrawlerSchedule>> GetById(Guid id, CancellationToken ct)
    {
        var row = await _db.GeekCrawlerSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost("{id:guid}/claim")]
    public async Task<ActionResult<GeekCrawlerSchedule>> ClaimDue(
        Guid id,
        [FromBody] ClaimGeekCrawlerScheduleCommand command,
        CancellationToken ct)
    {
        if (command is null || command.ExpectedNextRunUtc is null || command.NewNextRunUtc is null)
            return BadRequest("expectedNextRunUtc and newNextRunUtc are required");

        var now = DateTimeOffset.UtcNow;
        var row = await _db.GeekCrawlerSchedules.FirstOrDefaultAsync(
            s => s.Id == id
                 && s.Enabled
                 && s.NextRunUtc <= now
                 && s.NextRunUtc == command.ExpectedNextRunUtc,
            ct);

        if (row is null)
            return NotFound();

        row.NextRunUtc = command.NewNextRunUtc.Value;
        row.LastStartedUtc = command.LastStartedUtc ?? now;

        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpGet("due")]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerSchedule>>> ListDue(
        [FromQuery] DateTimeOffset beforeUtc,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var rows = await _db.GeekCrawlerSchedules.AsNoTracking()
            .Where(s => s.Enabled && s.NextRunUtc <= beforeUtc)
            .OrderBy(s => s.NextRunUtc)
            .Take(limit)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("for-user")]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerSchedule>>> ListForUser(
        [FromQuery] string ownerUserId,
        [FromQuery] string? crawlType = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return BadRequest("ownerUserId is required");

        limit = Math.Clamp(limit, 1, 200);
        var query = _db.GeekCrawlerSchedules.AsNoTracking()
            .Where(s => s.OwnerUserId == ownerUserId);

        if (!string.IsNullOrWhiteSpace(crawlType))
            query = query.Where(s => s.CrawlType == crawlType.Trim());

        var rows = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<GeekCrawlerSchedule>> Create(
        [FromBody] CreateGeekCrawlerScheduleCommand command,
        CancellationToken ct)
    {
        if (command is null
            || string.IsNullOrWhiteSpace(command.OwnerUserId)
            || string.IsNullOrWhiteSpace(command.CrawlType)
            || string.IsNullOrWhiteSpace(command.SeedUrlsJson))
            return BadRequest("ownerUserId, crawlType, and seedUrlsJson are required");

        var intervalHours = Math.Clamp(command.IntervalHours ?? 168, 1, 24 * 365);
        var row = new GeekCrawlerSchedule
        {
            Id = Guid.NewGuid(),
            OwnerUserId = command.OwnerUserId.Trim(),
            CrawlType = command.CrawlType.Trim(),
            SeedUrlsJson = command.SeedUrlsJson,
            SeedKey = command.SeedKey,
            IntervalHours = intervalHours,
            Enabled = command.Enabled ?? true,
            NextRunUtc = command.NextRunUtc ?? DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        _db.GeekCrawlerSchedules.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GeekCrawlerSchedule>> Patch(
        Guid id,
        [FromBody] PatchGeekCrawlerScheduleCommand command,
        CancellationToken ct)
    {
        var row = await _db.GeekCrawlerSchedules.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null) return NotFound();

        if (command.Enabled is not null)
            row.Enabled = command.Enabled.Value;
        if (command.IntervalHours is not null)
            row.IntervalHours = Math.Clamp(command.IntervalHours.Value, 1, 24 * 365);
        if (command.NextRunUtc is not null)
            row.NextRunUtc = command.NextRunUtc.Value;
        if (command.LastStartedUtc is not null)
            row.LastStartedUtc = command.LastStartedUtc;
        if (command.LastRunId is not null)
            row.LastRunId = command.LastRunId;

        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var row = await _db.GeekCrawlerSchedules.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row is null) return NotFound();

        _db.GeekCrawlerSchedules.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    public record CreateGeekCrawlerScheduleCommand(
        string OwnerUserId,
        string CrawlType,
        string SeedUrlsJson,
        string? SeedKey = null,
        int? IntervalHours = null,
        bool? Enabled = null,
        DateTimeOffset? NextRunUtc = null);

    public record PatchGeekCrawlerScheduleCommand(
        bool? Enabled = null,
        int? IntervalHours = null,
        DateTimeOffset? NextRunUtc = null,
        DateTimeOffset? LastStartedUtc = null,
        Guid? LastRunId = null);

    public record ClaimGeekCrawlerScheduleCommand(
        DateTimeOffset? ExpectedNextRunUtc,
        DateTimeOffset? NewNextRunUtc,
        DateTimeOffset? LastStartedUtc = null);
}
