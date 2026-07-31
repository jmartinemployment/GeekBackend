using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing reconciliation proposals. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/reconciliation")]
public class GcwReconciliationController : ControllerBase
{
    private static readonly HashSet<string> AllowedProposalTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "new-pain-point",
        "update-pain-point",
        "new-evidence-link",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwReconciliationController> _logger;

    public GcwReconciliationController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwReconciliationController> logger)
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
    public async Task<ActionResult<IReadOnlyList<ReconciliationProposalDto>>> List(
        [FromQuery] Guid researchRunId,
        CancellationToken ct)
    {
        if (researchRunId == Guid.Empty)
            return BadRequest("researchRunId is required");

        var proposals = await _repo.GetReconciliationProposalsByResearchRunIdAsync(researchRunId, ct);
        return Ok(proposals);
    }

    [HttpPost]
    public async Task<ActionResult<ReconciliationProposalDto>> Create(
        [FromBody] CreateGcwReconciliationRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.ResearchRunId == Guid.Empty)
            return BadRequest("researchRunId is required");
        if (string.IsNullOrWhiteSpace(request.ProposalType))
            return BadRequest("proposalType is required");

        var proposalType = request.ProposalType.Trim();
        if (!AllowedProposalTypes.Contains(proposalType))
            return BadRequest($"proposalType must be one of: {string.Join(", ", AllowedProposalTypes)}");

        proposalType = AllowedProposalTypes.First(t =>
            t.Equals(proposalType, StringComparison.OrdinalIgnoreCase));

        var proposedData = request.ProposedData ?? new Dictionary<string, object>();

        if (proposalType == "update-pain-point" &&
            (request.PainPointId is null || request.PainPointId == Guid.Empty))
            return BadRequest("painPointId is required for update-pain-point");

        _logger.LogInformation(
            "GCW user {UserId} creating reconciliation proposal for run {RunId}",
            _currentUser.UserId,
            request.ResearchRunId);

        var proposal = await _repo.CreateReconciliationProposalAsync(
            new CreateReconciliationProposalCommand(
                request.ResearchRunId,
                proposalType,
                request.PainPointId is null || request.PainPointId == Guid.Empty
                    ? null
                    : request.PainPointId,
                proposedData),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = proposal.Id }, proposal);
    }

    [HttpPatch("{id:guid}/approve")]
    public async Task<ActionResult<ReconciliationProposalDto>> Approve(Guid id, CancellationToken ct)
    {
        _logger.LogInformation(
            "GCW user {UserId} approving reconciliation proposal {ProposalId}",
            _currentUser.UserId,
            id);

        try
        {
            var proposal = await _repo.ApproveReconciliationProposalAsync(id, _currentUser.UserId, ct);
            return Ok(proposal);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    [HttpPatch("{id:guid}/dismiss")]
    public async Task<ActionResult<ReconciliationProposalDto>> Dismiss(Guid id, CancellationToken ct)
    {
        _logger.LogInformation(
            "GCW user {UserId} dismissing reconciliation proposal {ProposalId}",
            _currentUser.UserId,
            id);

        try
        {
            var proposal = await _repo.DismissReconciliationProposalAsync(id, _currentUser.UserId, ct);
            return Ok(proposal);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwReconciliationRequest(
        Guid ResearchRunId,
        string ProposalType,
        Dictionary<string, object>? ProposedData = null,
        Guid? PainPointId = null);
}
