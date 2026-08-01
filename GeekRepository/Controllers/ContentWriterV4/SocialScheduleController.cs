using GeekApplication.Interfaces.ContentWriterV4;
using GeekApplication.Models.ContentWriterV4;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV4;

[ApiController]
[Route("repo/content-writer-v4/social-schedule")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class SocialScheduleController : ControllerBase
{
    private readonly ISocialScheduleRepository _repository;

    public SocialScheduleController(ISocialScheduleRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SocialScheduleEntryDto>> GetById(Guid id, CancellationToken ct)
    {
        var entry = await _repository.GetByIdAsync(id, ct);
        if (entry is null)
            return NotFound();
        return Ok(entry);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SocialScheduleEntryDto>>> List(
        [FromQuery] Guid ownerId,
        [FromQuery] Guid? campaignId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        if (ownerId == Guid.Empty)
            return BadRequest("ownerId is required");

        var entries = await _repository.GetByOwnerIdAsync(ownerId, fromUtc, toUtc, campaignId, ct);
        return Ok(entries);
    }

    [HttpPost]
    public async Task<ActionResult<SocialScheduleEntryDto>> Create(
        [FromBody] CreateSocialScheduleEntryCommand command,
        CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");
        if (command.OwnerId == Guid.Empty)
            return BadRequest("ownerId is required");
        if (command.CampaignId == Guid.Empty || command.AssetVersionId == Guid.Empty)
            return BadRequest("campaignId and assetVersionId are required");
        if (string.IsNullOrWhiteSpace(command.Channel))
            return BadRequest("channel is required");
        if (string.IsNullOrWhiteSpace(command.Title))
            return BadRequest("title is required");

        var entry = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = entry.Id }, entry);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SocialScheduleEntryDto>> Update(
        Guid id,
        [FromBody] UpdateSocialScheduleEntryCommand command,
        CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");
        if (command.Id != id)
            return BadRequest("id mismatch");

        try
        {
            var entry = await _repository.UpdateAsync(command, ct);
            return Ok(entry);
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
}
