using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing campaign API. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/campaigns")]
public class GcwCampaignsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwCampaignsController> _logger;

    public GcwCampaignsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwCampaignsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentCampaignDto>> GetById(Guid id, CancellationToken ct)
    {
        var campaign = await _repo.GetCampaignByIdAsync(id, ct);
        if (campaign is null)
            return NotFound();
        return Ok(campaign);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentCampaignDto>>> List([FromQuery] Guid clientId, CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        var campaigns = await _repo.GetCampaignsByClientIdAsync(clientId, ct);
        return Ok(campaigns);
    }

    [HttpPost]
    public async Task<ActionResult<ContentCampaignDto>> Create([FromBody] CreateGcwCampaignRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.ClientId == Guid.Empty)
            return BadRequest("clientId is required");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");
        if (string.IsNullOrWhiteSpace(request.Keyword))
            return BadRequest("keyword is required");

        var profileVersionId = request.ProfileVersionId ?? Guid.Empty;
        if (profileVersionId == Guid.Empty)
        {
            var profile = await _repo.GetClientProfileByClientIdAsync(request.ClientId, ct);
            if (profile is not null)
            {
                var versions = await _repo.GetClientProfileVersionsByProfileIdAsync(profile.Id, ct);
                var latest = versions.OrderByDescending(v => v.Version).FirstOrDefault();
                if (latest is not null)
                    profileVersionId = latest.Id;
            }
        }

        var command = new CreateContentCampaignCommand(
            request.ClientId,
            request.Name.Trim(),
            request.Keyword.Trim(),
            profileVersionId);

        _logger.LogInformation(
            "GCW user {UserId} creating campaign for client {ClientId} (profileVersion={ProfileVersionId})",
            _currentUser.UserId,
            request.ClientId,
            profileVersionId);

        var campaign = await _repo.CreateCampaignAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = campaign.Id }, campaign);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ContentCampaignDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateGcwCampaignStatusRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("status is required");

        _logger.LogInformation(
            "GCW user {UserId} updating campaign {CampaignId} status to {Status}",
            _currentUser.UserId,
            id,
            request.Status);

        var campaign = await _repo.UpdateCampaignStatusAsync(id, request.Status.Trim(), ct);
        return Ok(campaign);
    }

    public sealed record CreateGcwCampaignRequest(
        Guid ClientId,
        string Name,
        string Keyword,
        Guid? ProfileVersionId = null);

    public sealed record UpdateGcwCampaignStatusRequest(string Status);
}
