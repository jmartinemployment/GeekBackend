using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

[ApiController]
[Route("repo/content-creator/versions")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccArtifactVersionsController : ControllerBase
{
    private readonly IGccArtifactVersionRepository _repository;
    private readonly ILogger<GccArtifactVersionsController> _logger;

    public GccArtifactVersionsController(IGccArtifactVersionRepository repository, ILogger<GccArtifactVersionsController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccArtifactVersionDto>> GetById(Guid id, CancellationToken ct)
    {
        var version = await _repository.GetByIdAsync(id, ct);
        if (version is null)
            return NotFound();

        return Ok(version);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccArtifactVersionDto>>> GetByArtifactId([FromQuery] Guid artifactId, CancellationToken ct)
    {
        if (artifactId == Guid.Empty)
            return BadRequest("artifactId is required");

        var versions = await _repository.GetByArtifactIdAsync(artifactId, ct);
        return Ok(versions);
    }

    [HttpPost]
    public async Task<ActionResult<GccArtifactVersionDto>> Create([FromBody] CreateGccArtifactVersionCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var version = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = version.Id }, version);
    }
}
