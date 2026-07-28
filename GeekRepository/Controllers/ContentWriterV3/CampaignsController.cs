using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/campaigns")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class CampaignsController : ControllerBase
{
    private readonly IContentCampaignRepository _repository;
    private readonly ILogger<CampaignsController> _logger;

    public CampaignsController(IContentCampaignRepository repository, ILogger<CampaignsController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentCampaignDto>> GetById(Guid id, CancellationToken ct)
    {
        var campaign = await _repository.GetByIdAsync(id, ct);
        if (campaign is null)
            return NotFound();

        return Ok(campaign);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentCampaignDto>>> GetByClientId([FromQuery] Guid clientId, CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        var campaigns = await _repository.GetByClientIdAsync(clientId, ct);
        return Ok(campaigns);
    }

    [HttpPost]
    public async Task<ActionResult<ContentCampaignDto>> Create([FromBody] CreateContentCampaignCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var campaign = await _repository.CreateAsync(command, ct);
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

        var campaign = await _repository.UpdateStatusAsync(id, request.Status, ct);
        return Ok(campaign);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _repository.DeleteAsync(id, ct);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    public record UpdateStatusRequest(string Status);
}
