using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Social;

[ApiController]
[Route("repo/social/drafts")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class SocialDraftsController : ControllerBase
{
    private readonly ISocialStore _social;

    public SocialDraftsController(ISocialStore social) => _social = social;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReplyDraftCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A draft is required.");
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.CreateDraftAsync(tenantId, command, ct));
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] Guid? accountId,
        [FromQuery] int limit = 0,
        CancellationToken ct = default)
    {
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.ListDraftsAsync(tenantId, status, accountId, limit, ct));
    }

    /// <summary>The drafts in one state across every tenant, for the background sweep of held sends.</summary>
    [HttpGet("across-tenants")]
    public async Task<IActionResult> ListAcrossTenants([FromQuery] string? status, [FromQuery] int limit = 0, CancellationToken ct = default) =>
        SocialResults.ToAction(this, await _social.ListDraftsAcrossTenantsAsync(status ?? string.Empty, limit, ct));

    [HttpGet("{draftId:guid}")]
    public async Task<IActionResult> Get(Guid draftId, CancellationToken ct)
    {
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.GetDraftAsync(tenantId, draftId, ct));
    }

    [HttpPut("{draftId:guid}/text")]
    public async Task<IActionResult> Edit(Guid draftId, [FromBody] EditReplyDraftCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("An edit is required.");
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.EditDraftAsync(tenantId, draftId, command, ct));
    }

    /// <summary>409 when the draft is no longer in the state the caller read: the other caller won.</summary>
    [HttpPost("{draftId:guid}/transition")]
    public async Task<IActionResult> Transition(Guid draftId, [FromBody] TransitionReplyDraftCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A transition is required.");
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.TransitionDraftAsync(tenantId, draftId, command, ct));
    }
}
