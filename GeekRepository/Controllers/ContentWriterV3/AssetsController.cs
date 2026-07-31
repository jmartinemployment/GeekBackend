using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/assets")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class AssetsController : ControllerBase
{
    private readonly IContentAssetRepository _repository;

    public AssetsController(IContentAssetRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentAssetDto>> GetById(Guid id, CancellationToken ct)
    {
        var asset = await _repository.GetByIdAsync(id, ct);
        if (asset is null)
            return NotFound();

        return Ok(asset);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentAssetDto>>> GetByCampaignId([FromQuery] Guid campaignId, CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        var assets = await _repository.GetByCampaignIdAsync(campaignId, ct);
        return Ok(assets);
    }

    [HttpPost]
    public async Task<ActionResult<ContentAssetDto>> Create([FromBody] CreateContentAssetCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var asset = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = asset.Id }, asset);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ContentAssetDto>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("status is required");

        try
        {
            var asset = await _repository.UpdateStatusAsync(id, request.Status.Trim(), ct);
            return Ok(asset);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await _repository.DeleteAsync(id, ct);
        if (!success)
            return NotFound();

        return NoContent();
    }

    public record UpdateStatusRequest(string Status);
}
