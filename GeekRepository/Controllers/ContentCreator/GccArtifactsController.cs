using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

[ApiController]
[Route("repo/content-creator/artifacts")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccArtifactsController : ControllerBase
{
    private readonly IGccArtifactRepository _repository;
    private readonly ILogger<GccArtifactsController> _logger;

    public GccArtifactsController(IGccArtifactRepository repository, ILogger<GccArtifactsController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccArtifactDto>> GetById(Guid id, CancellationToken ct)
    {
        var artifact = await _repository.GetByIdAsync(id, ct);
        if (artifact is null)
            return NotFound();

        return Ok(artifact);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccArtifactDto>>> GetByCreateId([FromQuery] Guid createId, CancellationToken ct)
    {
        if (createId == Guid.Empty)
            return BadRequest("createId is required");

        var artifacts = await _repository.GetByCreateIdAsync(createId, ct);
        return Ok(artifacts);
    }

    /// <summary>Every draft on a project, newest first. An empty list is a project with no drafts yet.</summary>
    [HttpGet("~/repo/content-creator/projects/{projectId:guid}/artifacts")]
    public async Task<ActionResult<IReadOnlyList<GccArtifactDto>>> GetByProjectId(Guid projectId, CancellationToken ct) =>
        Ok(await _repository.GetByProjectIdAsync(projectId, ct));

    [HttpPost]
    public async Task<ActionResult<GccArtifactDto>> Create([FromBody] CreateGccArtifactCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var artifact = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = artifact.Id }, artifact);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<GccArtifactDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateStatusRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("Status is required");

        try
        {
            var artifact = await _repository.UpdateStatusAsync(id, request.Status, ct);
            return Ok(artifact);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    public record UpdateStatusRequest(string Status);
}
