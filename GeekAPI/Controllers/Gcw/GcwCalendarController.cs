using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV4;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// Metricool-class social calendar for GCW. Persists via CWV4 social_schedule_entries.
/// </summary>
[ApiController]
[Route("api/gcw/calendar")]
public class GcwCalendarController : ControllerBase
{
    private static readonly HashSet<string> AllowedChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "linkedin",
        "x",
        "instagram",
        "facebook",
        "youtube",
        "email",
        "blog",
    };

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "scheduled",
        "posted",
        "cancelled",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwCalendarController> _logger;

    public GcwCalendarController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwCalendarController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("entries")]
    public async Task<ActionResult<IReadOnlyList<SocialScheduleEntryDto>>> List(
        [FromQuery] Guid? campaignId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        var entries = await _repo.GetSocialScheduleByOwnerIdAsync(
            _currentUser.UserId,
            fromUtc,
            toUtc,
            campaignId,
            ct);
        return Ok(entries);
    }

    [HttpPost("entries")]
    public async Task<ActionResult<SocialScheduleEntryDto>> Create(
        [FromBody] CreateGcwCalendarEntryRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.CampaignId == Guid.Empty || request.AssetVersionId == Guid.Empty)
            return BadRequest("campaignId and assetVersionId are required");
        if (string.IsNullOrWhiteSpace(request.Channel))
            return BadRequest("channel is required");

        var channel = request.Channel.Trim().ToLowerInvariant();
        if (!AllowedChannels.Contains(channel))
            return BadRequest($"channel must be one of: {string.Join(", ", AllowedChannels)}");

        var version = await _repo.GetAssetVersionByIdAsync(request.AssetVersionId, ct);
        if (version is null)
            return NotFound("asset version not found");

        var asset = await _repo.GetAssetByIdAsync(version.AssetId, ct);
        if (asset is null)
            return NotFound("asset not found");
        if (asset.CampaignId != request.CampaignId)
            return BadRequest("asset does not belong to campaignId");

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? asset.Name
            : request.Title.Trim();

        _logger.LogInformation(
            "GCW user {UserId} scheduling {Channel} for asset {AssetId} at {When}",
            _currentUser.UserId,
            channel,
            asset.Id,
            request.ScheduledAtUtc);

        var entry = await _repo.CreateSocialScheduleEntryAsync(
            new CreateSocialScheduleEntryCommand(
                _currentUser.UserId,
                request.CampaignId,
                asset.Id,
                request.AssetVersionId,
                channel,
                request.ScheduledAtUtc.ToUniversalTime(),
                title,
                string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()),
            ct);

        return CreatedAtAction(nameof(List), new { campaignId = entry.CampaignId }, entry);
    }

    [HttpPatch("entries/{id:guid}")]
    public async Task<ActionResult<SocialScheduleEntryDto>> Update(
        Guid id,
        [FromBody] UpdateGcwCalendarEntryRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");

        var existing = await _repo.GetSocialScheduleEntryByIdAsync(id, ct);
        if (existing is null)
            return NotFound();
        if (existing.OwnerId != _currentUser.UserId)
            return Forbid();

        var channel = string.IsNullOrWhiteSpace(request.Channel)
            ? existing.Channel
            : request.Channel.Trim().ToLowerInvariant();
        if (!AllowedChannels.Contains(channel))
            return BadRequest($"channel must be one of: {string.Join(", ", AllowedChannels)}");

        var status = string.IsNullOrWhiteSpace(request.Status)
            ? existing.Status
            : request.Status.Trim().ToLowerInvariant();
        if (!AllowedStatuses.Contains(status))
            return BadRequest($"status must be one of: {string.Join(", ", AllowedStatuses)}");

        var scheduledAt = request.ScheduledAtUtc ?? existing.ScheduledAtUtc;
        var title = string.IsNullOrWhiteSpace(request.Title) ? existing.Title : request.Title.Trim();
        var notes = request.Notes is null ? existing.Notes : request.Notes.Trim();

        var updated = await _repo.UpdateSocialScheduleEntryAsync(
            new UpdateSocialScheduleEntryCommand(
                id,
                channel,
                scheduledAt.ToUniversalTime(),
                status,
                title,
                string.IsNullOrWhiteSpace(notes) ? null : notes),
            ct);
        return Ok(updated);
    }

    [HttpDelete("entries/{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var existing = await _repo.GetSocialScheduleEntryByIdAsync(id, ct);
        if (existing is null)
            return NotFound();
        if (existing.OwnerId != _currentUser.UserId)
            return Forbid();

        await _repo.DeleteSocialScheduleEntryAsync(id, ct);
        return NoContent();
    }

    public sealed record CreateGcwCalendarEntryRequest(
        Guid CampaignId,
        Guid AssetVersionId,
        string Channel,
        DateTime ScheduledAtUtc,
        string? Title = null,
        string? Notes = null);

    public sealed record UpdateGcwCalendarEntryRequest(
        string? Channel = null,
        DateTime? ScheduledAtUtc = null,
        string? Status = null,
        string? Title = null,
        string? Notes = null);
}
