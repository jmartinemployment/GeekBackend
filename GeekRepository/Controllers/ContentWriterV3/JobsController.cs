using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/jobs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class JobsController : ControllerBase
{
    private readonly IJobRepository _repository;

    public JobsController(IJobRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobDto>> GetById(Guid id, CancellationToken ct)
    {
        var job = await _repository.GetByIdAsync(id, ct);
        if (job is null)
            return NotFound();

        return Ok(job);
    }

    [HttpGet("by-status/{status}")]
    public async Task<ActionResult<IReadOnlyList<JobDto>>> GetByStatus(string status, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(status))
            return BadRequest("status is required");

        var jobs = await _repository.GetByStatusAsync(status, limit, ct);
        return Ok(jobs);
    }

    [HttpPost]
    public async Task<ActionResult<JobDto>> Create([FromBody] CreateJobCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var job = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<JobDto>> UpdateStatus(Guid id, [FromBody] UpdateJobStatusCommand command, CancellationToken ct)
    {
        var job = await _repository.UpdateStatusAsync(command, ct);
        return Ok(job);
    }

    [HttpPost("{id:guid}/lease")]
    public async Task<ActionResult<JobDto>> LeaseJob(Guid id, [FromBody] LeaseRequest request, CancellationToken ct)
    {
        var job = await _repository.LeaseAsync(id, request.LeaseOwner, request.Duration, ct);
        return Ok(job);
    }

    [HttpPost("{id:guid}/release-lease")]
    public async Task<ActionResult<JobDto>> ReleaseJobLease(Guid id, CancellationToken ct)
    {
        var job = await _repository.ReleaseLeaseAsync(id, ct);
        return Ok(job);
    }

    public record LeaseRequest(string LeaseOwner, TimeSpan Duration);
}

[ApiController]
[Route("repo/content-writer-v3/pain-point-evidence-links")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class PainPointEvidenceLinksController : ControllerBase
{
    private readonly IPainPointEvidenceLinkRepository _repository;

    public PainPointEvidenceLinksController(IPainPointEvidenceLinkRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PainPointEvidenceLinkDto>> GetById(Guid id, CancellationToken ct)
    {
        var link = await _repository.GetByIdAsync(id, ct);
        if (link is null)
            return NotFound();

        return Ok(link);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PainPointEvidenceLinkDto>>> GetByPainPointId([FromQuery] Guid painPointId, CancellationToken ct)
    {
        if (painPointId == Guid.Empty)
            return BadRequest("painPointId is required");

        var links = await _repository.GetByPainPointIdAsync(painPointId, ct);
        return Ok(links);
    }

    [HttpPost]
    public async Task<ActionResult<PainPointEvidenceLinkDto>> Create([FromBody] CreatePainPointEvidenceLinkCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var link = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = link.Id }, link);
    }
}
