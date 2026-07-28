using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/research-runs")]
public class ResearchRunsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ResearchRunsController> _logger;

    public ResearchRunsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<ResearchRunsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchRunDto>> GetById(Guid id, CancellationToken ct)
    {
        var run = await _repo.GetResearchRunByIdAsync(id, ct);
        if (run is null)
            return NotFound();

        return Ok(run);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResearchRunDto>>> GetByCampaignId([FromQuery] Guid campaignId, CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        _logger.LogInformation("User {UserId} fetching research runs for campaign {CampaignId}",
            _currentUser.UserId, campaignId);

        var runs = await _repo.GetResearchRunsByCampaignIdAsync(campaignId, ct);
        return Ok(runs);
    }

    [HttpPost]
    public async Task<ActionResult<ResearchRunDto>> Create([FromBody] CreateResearchRunCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating research run for campaign {CampaignId}",
            _currentUser.UserId, command.CampaignId);

        var run = await _repo.CreateResearchRunAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = run.Id }, run);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ResearchRunDto>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} updating research run {RunId} status to {Status}",
            _currentUser.UserId, id, request.Status);

        var run = await _repo.UpdateResearchRunStatusAsync(id, request.Status, request.DiscoveredSourceCount, request.SpentBudget, request.ErrorMessage, ct);
        return Ok(run);
    }

    public record UpdateStatusRequest(string Status, int DiscoveredSourceCount, decimal SpentBudget, string? ErrorMessage);
}

[ApiController]
[Route("api/content-writer/v3/research-sources")]
public class ResearchSourcesController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ResearchSourcesController> _logger;

    public ResearchSourcesController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<ResearchSourcesController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchSourceDto>> GetById(Guid id, CancellationToken ct)
    {
        var source = await _repo.GetResearchSourceByIdAsync(id, ct);
        if (source is null)
            return NotFound();

        return Ok(source);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResearchSourceDto>>> GetByResearchRunId([FromQuery] Guid researchRunId, CancellationToken ct)
    {
        if (researchRunId == Guid.Empty)
            return BadRequest("researchRunId is required");

        _logger.LogInformation("User {UserId} fetching research sources for run {ResearchRunId}",
            _currentUser.UserId, researchRunId);

        var sources = await _repo.GetResearchSourcesByRunIdAsync(researchRunId, ct);
        return Ok(sources);
    }

    [HttpPost]
    public async Task<ActionResult<ResearchSourceDto>> Create([FromBody] CreateResearchSourceCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating research source for run {ResearchRunId}",
            _currentUser.UserId, command.ResearchRunId);

        var source = await _repo.CreateResearchSourceAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = source.Id }, source);
    }
}

[ApiController]
[Route("api/content-writer/v3/research-evidence")]
public class ResearchEvidenceController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ResearchEvidenceController> _logger;

    public ResearchEvidenceController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<ResearchEvidenceController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchEvidenceDto>> GetById(Guid id, CancellationToken ct)
    {
        var evidence = await _repo.GetResearchEvidenceByIdAsync(id, ct);
        if (evidence is null)
            return NotFound();

        return Ok(evidence);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResearchEvidenceDto>>> GetBySourceId([FromQuery] Guid sourceId, CancellationToken ct)
    {
        if (sourceId == Guid.Empty)
            return BadRequest("sourceId is required");

        _logger.LogInformation("User {UserId} fetching research evidence for source {SourceId}",
            _currentUser.UserId, sourceId);

        var evidence = await _repo.GetResearchEvidenceBySourceIdAsync(sourceId, ct);
        return Ok(evidence);
    }

    [HttpPost]
    public async Task<ActionResult<ResearchEvidenceDto>> Create([FromBody] CreateResearchEvidenceCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating research evidence for source {SourceId}",
            _currentUser.UserId, command.ResearchSourceId);

        var evidence = await _repo.CreateResearchEvidenceAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = evidence.Id }, evidence);
    }

    [HttpPatch("{id:guid}/approve")]
    public async Task<ActionResult<ResearchEvidenceDto>> Approve(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} approving research evidence {EvidenceId}",
            _currentUser.UserId, id);

        var evidence = await _repo.ApproveResearchEvidenceAsync(id, ct);
        return Ok(evidence);
    }
}
