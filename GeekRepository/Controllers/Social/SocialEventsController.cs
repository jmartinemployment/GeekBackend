using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Social;

[ApiController]
[Route("repo/social/events")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class SocialEventsController : ControllerBase
{
    private readonly ISocialStore _social;

    public SocialEventsController(ISocialStore social) => _social = social;

    /// <summary>200 with inserted true for a new event and inserted false for a redelivery; either way the store holds exactly one.</summary>
    [HttpPost]
    public async Task<IActionResult> Record([FromBody] EngagementEvent engagementEvent, CancellationToken ct)
    {
        if (engagementEvent is null) return BadRequest("An event is required.");
        var result = await _social.RecordEventAsync(engagementEvent, ct);
        return result.IsSuccess
            ? Ok(new SocialEventStored { Inserted = result.Value })
            : SocialResults.Failed(this, result.ErrorCode, result.Error);
    }

    /// <summary>The comments no tenant's Triage has decided, across every tenant, for recovery after a restart.</summary>
    [HttpGet("untriaged")]
    public async Task<IActionResult> ListUntriaged([FromQuery] int limit = 0, CancellationToken ct = default) =>
        SocialResults.ToAction(this, await _social.ListUntriagedEventsAsync(limit, ct));

    /// <summary>One event with its tenant, for the orchestrator to learn whose it is from a webhook wake.</summary>
    [HttpGet("{eventId:guid}/across-tenants")]
    public async Task<IActionResult> GetAcrossTenants(Guid eventId, CancellationToken ct) =>
        SocialResults.ToAction(this, await _social.GetEventAcrossTenantsAsync(eventId, ct));

    [HttpGet("{eventId:guid}")]
    public async Task<IActionResult> Get(Guid eventId, CancellationToken ct)
    {
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.GetEventAsync(tenantId, eventId, ct));
    }

    /// <summary>Events by where Triage left them. triageState is required: a triage outcome, or untriaged for the comments with none.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? triageState,
        [FromQuery] Guid? accountId,
        [FromQuery] int limit = 0,
        CancellationToken ct = default)
    {
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.ListEventsAsync(tenantId, triageState ?? string.Empty, accountId, limit, ct));
    }

    /// <summary>409 when the event's triage is already decided and not one an operator may re-run.</summary>
    [HttpPost("{eventId:guid}/triage")]
    public async Task<IActionResult> RecordTriage(Guid eventId, [FromBody] RecordTriageOutcomeCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("An outcome is required.");
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.RecordTriageOutcomeAsync(tenantId, eventId, command, ct));
    }
}
