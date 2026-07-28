using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/campaigns")]
public class CampaignsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<CampaignsController> _logger;

    public CampaignsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<CampaignsController> logger)
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
    public async Task<ActionResult<IReadOnlyList<ContentCampaignDto>>> GetByClientId([FromQuery] Guid clientId, CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        var campaigns = await _repo.GetCampaignsByClientIdAsync(clientId, ct);
        return Ok(campaigns);
    }

    [HttpPost]
    public async Task<ActionResult<ContentCampaignDto>> Create([FromBody] CreateContentCampaignCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        _logger.LogInformation("User {UserId} creating campaign for client {ClientId}", _currentUser.UserId, command.ClientId);

        var campaign = await _repo.CreateCampaignAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = campaign.Id }, campaign);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ContentCampaignDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateStatusRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("Status is required");

        _logger.LogInformation("User {UserId} updating campaign {CampaignId} status to {Status}",
            _currentUser.UserId, id, request.Status);

        var campaign = await _repo.UpdateCampaignStatusAsync(id, request.Status, ct);
        return Ok(campaign);
    }

    public record UpdateStatusRequest(string Status);
}
