using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/reconciliation")]
public class ReconciliationController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ReconciliationController> _logger;

    public ReconciliationController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<ReconciliationController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReconciliationProposalDto>> GetById(Guid id, CancellationToken ct)
    {
        var proposal = await _repo.GetReconciliationProposalByIdAsync(id, ct);
        if (proposal is null)
            return NotFound();

        return Ok(proposal);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReconciliationProposalDto>>> GetByResearchRunId(
        [FromQuery] Guid researchRunId,
        CancellationToken ct)
    {
        if (researchRunId == Guid.Empty)
            return BadRequest("researchRunId is required");

        _logger.LogInformation("User {UserId} fetching reconciliation proposals for research run {ResearchRunId}",
            _currentUser.UserId, researchRunId);

        var proposals = await _repo.GetReconciliationProposalsByResearchRunIdAsync(researchRunId, ct);
        return Ok(proposals);
    }

    [HttpPost]
    public async Task<ActionResult<ReconciliationProposalDto>> Create([FromBody] CreateReconciliationProposalCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating reconciliation proposal for research run {ResearchRunId}",
            _currentUser.UserId, command.ResearchRunId);

        var proposal = await _repo.CreateReconciliationProposalAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = proposal.Id }, proposal);
    }

    [HttpPatch("{id:guid}/approve")]
    public async Task<ActionResult<ReconciliationProposalDto>> Approve(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} approving reconciliation proposal {ProposalId}",
            _currentUser.UserId, id);

        var proposal = await _repo.ApproveReconciliationProposalAsync(id, _currentUser.UserId, ct);
        return Ok(proposal);
    }

    [HttpPatch("{id:guid}/dismiss")]
    public async Task<ActionResult<ReconciliationProposalDto>> Dismiss(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} dismissing reconciliation proposal {ProposalId}",
            _currentUser.UserId, id);

        var proposal = await _repo.DismissReconciliationProposalAsync(id, _currentUser.UserId, ct);
        return Ok(proposal);
    }
}
