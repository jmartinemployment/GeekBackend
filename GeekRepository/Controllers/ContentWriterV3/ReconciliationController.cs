using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/reconciliation")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ReconciliationController : ControllerBase
{
    private readonly IReconciliationProposalRepository _repository;

    public ReconciliationController(IReconciliationProposalRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReconciliationProposalDto>> GetById(Guid id, CancellationToken ct)
    {
        var proposal = await _repository.GetByIdAsync(id, ct);
        if (proposal is null)
            return NotFound();

        return Ok(proposal);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReconciliationProposalDto>>> GetByResearchRunId([FromQuery] Guid researchRunId, CancellationToken ct)
    {
        if (researchRunId == Guid.Empty)
            return BadRequest("researchRunId is required");

        var proposals = await _repository.GetByResearchRunIdAsync(researchRunId, ct);
        return Ok(proposals);
    }

    [HttpPost]
    public async Task<ActionResult<ReconciliationProposalDto>> Create([FromBody] CreateReconciliationProposalCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var proposal = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = proposal.Id }, proposal);
    }

    [HttpPatch("{id:guid}/approve")]
    public async Task<ActionResult<ReconciliationProposalDto>> Approve(Guid id, [FromBody] ApproveRequest request, CancellationToken ct)
    {
        var proposal = await _repository.ApproveAsync(id, request.UserId, ct);
        return Ok(proposal);
    }

    [HttpPatch("{id:guid}/dismiss")]
    public async Task<ActionResult<ReconciliationProposalDto>> Dismiss(Guid id, [FromBody] DismissRequest request, CancellationToken ct)
    {
        var proposal = await _repository.DismissAsync(id, request.UserId, ct);
        return Ok(proposal);
    }

    public record ApproveRequest(Guid UserId);
    public record DismissRequest(Guid UserId);
}
