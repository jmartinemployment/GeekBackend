using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/asset-versions")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ContentAssetVersionController : ControllerBase
{
    private readonly IContentAssetVersionRepository _repository;

    public ContentAssetVersionController(IContentAssetVersionRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentAssetVersionDto>> GetById(Guid id, CancellationToken ct)
    {
        var version = await _repository.GetByIdAsync(id, ct);
        if (version is null)
            return NotFound();

        return Ok(version);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentAssetVersionDto>>> GetByAssetId([FromQuery] Guid assetId, CancellationToken ct)
    {
        if (assetId == Guid.Empty)
            return BadRequest("assetId is required");

        var versions = await _repository.GetByAssetIdAsync(assetId, ct);
        return Ok(versions);
    }

    [HttpPost]
    public async Task<ActionResult<ContentAssetVersionDto>> Create([FromBody] CreateContentAssetVersionCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var version = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = version.Id }, version);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ContentAssetVersionDto>> Update(Guid id, [FromBody] UpdateContentAssetVersionCommand command, CancellationToken ct)
    {
        var version = await _repository.UpdateAsync(command, ct);
        return Ok(version);
    }
}
