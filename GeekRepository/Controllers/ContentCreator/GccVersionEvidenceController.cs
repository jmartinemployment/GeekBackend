using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

/// <summary>What each artifact version was made from, one row per version.</summary>
[ApiController]
[Route("repo/content-creator/version-evidence")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccVersionEvidenceController(IGccVersionEvidenceRepository repository) : ControllerBase
{
    [HttpGet("by-version/{versionId:guid}")]
    public async Task<ActionResult<GccVersionEvidenceDto>> GetByVersion(Guid versionId, CancellationToken ct)
    {
        var evidence = await repository.GetByVersionIdAsync(versionId, ct);
        return evidence is null ? NotFound() : Ok(evidence);
    }

    [HttpPost]
    public async Task<ActionResult<GccVersionEvidenceDto>> Create(
        [FromBody] CreateGccVersionEvidenceCommand request, CancellationToken ct)
    {
        if (request is null) return BadRequest("Body required");
        if (request.VersionId == Guid.Empty) return BadRequest("versionId required");
        if (string.IsNullOrWhiteSpace(request.Provider)) return BadRequest("provider required");
        if (string.IsNullOrWhiteSpace(request.ModelIdsJson)) return BadRequest("modelIdsJson required");
        if (string.IsNullOrWhiteSpace(request.CallsJson)) return BadRequest("callsJson required");

        var result = await repository.CreateAsync(request, ct);
        return result.Evidence is null ? Conflict(result.Refusal) : Ok(result.Evidence);
    }
}
