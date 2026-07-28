using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/research-runs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ResearchRunsController : ControllerBase
{
    private readonly IResearchRunRepository _repository;

    public ResearchRunsController(IResearchRunRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchRunDto>> GetById(Guid id, CancellationToken ct)
    {
        var run = await _repository.GetByIdAsync(id, ct);
        if (run is null)
            return NotFound();

        return Ok(run);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResearchRunDto>>> GetByCampaignId([FromQuery] Guid campaignId, CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        var runs = await _repository.GetByCampaignIdAsync(campaignId, ct);
        return Ok(runs);
    }

    [HttpPost]
    public async Task<ActionResult<ResearchRunDto>> Create([FromBody] CreateResearchRunCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var run = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = run.Id }, run);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ResearchRunDto>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        var run = await _repository.UpdateStatusAsync(
            new UpdateResearchRunStatusCommand(id, request.Status, request.DiscoveredSourceCount, request.SpentBudget, request.ErrorMessage), ct);
        return Ok(run);
    }

    public record UpdateStatusRequest(string Status, int DiscoveredSourceCount, decimal SpentBudget, string? ErrorMessage);
}

[ApiController]
[Route("repo/content-writer-v3/research-sources")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ResearchSourcesController : ControllerBase
{
    private readonly IResearchSourceRepository _repository;

    public ResearchSourcesController(IResearchSourceRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchSourceDto>> GetById(Guid id, CancellationToken ct)
    {
        var source = await _repository.GetByIdAsync(id, ct);
        if (source is null)
            return NotFound();

        return Ok(source);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResearchSourceDto>>> GetByResearchRunId([FromQuery] Guid researchRunId, CancellationToken ct)
    {
        if (researchRunId == Guid.Empty)
            return BadRequest("researchRunId is required");

        var sources = await _repository.GetByResearchRunIdAsync(researchRunId, ct);
        return Ok(sources);
    }

    [HttpPost]
    public async Task<ActionResult<ResearchSourceDto>> Create([FromBody] CreateResearchSourceCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var source = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = source.Id }, source);
    }
}

[ApiController]
[Route("repo/content-writer-v3/research-evidence")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ResearchEvidenceController : ControllerBase
{
    private readonly IResearchEvidenceRepository _repository;

    public ResearchEvidenceController(IResearchEvidenceRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchEvidenceDto>> GetById(Guid id, CancellationToken ct)
    {
        var evidence = await _repository.GetByIdAsync(id, ct);
        if (evidence is null)
            return NotFound();

        return Ok(evidence);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResearchEvidenceDto>>> GetBySourceId([FromQuery] Guid sourceId, CancellationToken ct)
    {
        if (sourceId == Guid.Empty)
            return BadRequest("sourceId is required");

        var evidence = await _repository.GetBySourceIdAsync(sourceId, ct);
        return Ok(evidence);
    }

    [HttpPost]
    public async Task<ActionResult<ResearchEvidenceDto>> Create([FromBody] CreateResearchEvidenceCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var evidence = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = evidence.Id }, evidence);
    }

    [HttpPatch("{id:guid}/approve")]
    public async Task<ActionResult<ResearchEvidenceDto>> Approve(Guid id, CancellationToken ct)
    {
        var evidence = await _repository.ApproveAsync(id, ct);
        return Ok(evidence);
    }
}
