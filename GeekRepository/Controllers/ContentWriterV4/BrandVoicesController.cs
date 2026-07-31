using GeekApplication.Interfaces.ContentWriterV4;
using GeekApplication.Models.ContentWriterV4;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV4;

[ApiController]
[Route("repo/content-writer-v4/brand-voices")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class BrandVoicesController : ControllerBase
{
    private readonly IBrandVoiceRepository _repository;

    public BrandVoicesController(IBrandVoiceRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BrandVoiceDto>> GetById(Guid id, CancellationToken ct)
    {
        var voice = await _repository.GetByIdAsync(id, ct);
        if (voice is null)
            return NotFound();
        return Ok(voice);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BrandVoiceDto>>> List(
        [FromQuery] Guid ownerId,
        CancellationToken ct)
    {
        if (ownerId == Guid.Empty)
            return BadRequest("ownerId is required");

        var voices = await _repository.GetByOwnerIdAsync(ownerId, ct);
        return Ok(voices);
    }

    [HttpPost]
    public async Task<ActionResult<BrandVoiceDto>> Create(
        [FromBody] CreateBrandVoiceCommand command,
        CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");
        if (command.OwnerId == Guid.Empty)
            return BadRequest("ownerId is required");
        if (string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("name is required");

        var voice = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = voice.Id }, voice);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BrandVoiceDto>> Update(
        Guid id,
        [FromBody] UpdateBrandVoiceCommand command,
        CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");
        if (command.Id != id)
            return BadRequest("id mismatch");

        try
        {
            var voice = await _repository.UpdateAsync(command, ct);
            return Ok(voice);
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
