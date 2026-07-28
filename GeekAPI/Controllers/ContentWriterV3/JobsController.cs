using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/jobs")]
public class JobsApiController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<JobsApiController> _logger;

    public JobsApiController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<JobsApiController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobDto>> GetById(Guid id, CancellationToken ct)
    {
        var job = await _repo.GetJobByIdAsync(id, ct);
        if (job is null)
            return NotFound();

        return Ok(job);
    }

    [HttpGet("by-status/{status}")]
    public async Task<ActionResult<IReadOnlyList<JobDto>>> GetByStatus(string status, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(status))
            return BadRequest("status is required");

        _logger.LogInformation("User {UserId} fetching jobs by status {Status}", _currentUser.UserId, status);

        var jobs = await _repo.GetJobsByStatusAsync(status, limit, ct);
        return Ok(jobs);
    }

    [HttpPost]
    public async Task<ActionResult<JobDto>> Create([FromBody] CreateJobCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating job", _currentUser.UserId);

        var job = await _repo.CreateJobAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job);
    }

    [HttpPost("{id:guid}/lease")]
    public async Task<ActionResult<JobDto>> LeaseJob(Guid id, [FromBody] LeaseJobRequest request, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} leasing job {JobId}", _currentUser.UserId, id);

        var job = await _repo.LeaseJobAsync(id, request.LeaseOwner, request.LeaseDuration, ct);
        return Ok(job);
    }

    [HttpPost("{id:guid}/release-lease")]
    public async Task<ActionResult<JobDto>> ReleaseJobLease(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} releasing job lease {JobId}", _currentUser.UserId, id);

        var job = await _repo.ReleaseJobLeaseAsync(id, ct);
        return Ok(job);
    }

    public record LeaseJobRequest(string LeaseOwner, TimeSpan LeaseDuration);
}

[ApiController]
[Route("api/content-writer/v3/pain-point-evidence-links")]
public class PainPointEvidenceLinksApiController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<PainPointEvidenceLinksApiController> _logger;

    public PainPointEvidenceLinksApiController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<PainPointEvidenceLinksApiController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PainPointEvidenceLinkDto>> GetById(Guid id, CancellationToken ct)
    {
        var link = await _repo.GetPainPointEvidenceLinkByIdAsync(id, ct);
        if (link is null)
            return NotFound();

        return Ok(link);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PainPointEvidenceLinkDto>>> GetByPainPointId([FromQuery] Guid painPointId, CancellationToken ct)
    {
        if (painPointId == Guid.Empty)
            return BadRequest("painPointId is required");

        _logger.LogInformation("User {UserId} fetching pain point evidence links for pain point {PainPointId}",
            _currentUser.UserId, painPointId);

        var links = await _repo.GetPainPointEvidenceLinksByPainPointIdAsync(painPointId, ct);
        return Ok(links);
    }

    [HttpPost]
    public async Task<ActionResult<PainPointEvidenceLinkDto>> Create([FromBody] CreatePainPointEvidenceLinkCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating pain point evidence link for pain point {PainPointId}",
            _currentUser.UserId, command.PainPointId);

        var link = await _repo.CreatePainPointEvidenceLinkAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = link.Id }, link);
    }
}
