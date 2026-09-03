using GeekRepository.Auth;
using GeekRepository.Data.Entities.GeekCrawler;
using GeekRepository.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.GeekCrawler;

[ApiController]
[Route("repo/geek-crawler/schedules")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GeekCrawlerSchedulesController : ControllerBase
{
    private readonly IMongoGeekCrawlerService _mongo;

    public GeekCrawlerSchedulesController(IMongoGeekCrawlerService mongo) => _mongo = mongo;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GeekCrawlerSchedule>> GetById(Guid id, CancellationToken ct)
    {
        var row = await _mongo.GetScheduleByIdAsync(id, ct);
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

        var row = await _mongo.ClaimDueScheduleAsync(id, command.ExpectedNextRunUtc.Value, command.NewNextRunUtc.Value, command.LastStartedUtc, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpGet("due")]
    public async Task<ActionResult<IReadOnlyList<GeekCrawlerSchedule>>> ListDue(
        [FromQuery] DateTimeOffset beforeUtc,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var rows = await _mongo.ListDueSchedulesAsync(beforeUtc, limit, ct);
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
        var rows = await _mongo.ListSchedulesForUserAsync(ownerUserId, crawlType, limit, ct);
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

        var created = await _mongo.CreateScheduleAsync(row, ct);
        return Ok(created);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GeekCrawlerSchedule>> Patch(
        Guid id,
        [FromBody] PatchGeekCrawlerScheduleCommand command,
        CancellationToken ct)
    {
        try
        {
            GeekCrawlerSchedule? row = null;
            await _mongo.UpdateScheduleAsync(id, s =>
            {
                if (command.Enabled is not null)
                    s.Enabled = command.Enabled.Value;
                if (command.IntervalHours is not null)
                    s.IntervalHours = Math.Clamp(command.IntervalHours.Value, 1, 24 * 365);
                if (command.NextRunUtc is not null)
                    s.NextRunUtc = command.NextRunUtc.Value;
                if (command.LastStartedUtc is not null)
                    s.LastStartedUtc = command.LastStartedUtc;
                if (command.LastRunId is not null)
                    s.LastRunId = command.LastRunId;
                row = s;
            }, ct);
            return Ok(row);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var row = await _mongo.GetScheduleByIdAsync(id, ct);
        if (row is null) return NotFound();

        await _mongo.DeleteScheduleAsync(id, ct);
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
