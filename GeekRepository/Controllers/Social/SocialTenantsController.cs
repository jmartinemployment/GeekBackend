using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Social;

[ApiController]
[Route("repo/social/tenants")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class SocialTenantsController : ControllerBase
{
    private readonly ISocialStore _social;

    public SocialTenantsController(ISocialStore social) => _social = social;

    /// <summary>404 when the person has no tenant yet. A tenant is made by an explicit sign-up, never by reading.</summary>
    [HttpGet("by-owner/{ownerUserId:guid}")]
    public async Task<IActionResult> GetByOwner(Guid ownerUserId, CancellationToken ct) =>
        SocialResults.ToAction(this, await _social.GetTenantByOwnerAsync(ownerUserId, ct));

    [HttpGet("{tenantId:guid}")]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct) =>
        SocialResults.ToAction(this, await _social.GetTenantAsync(tenantId, ct));

    /// <summary>409 when the person already has a tenant.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSocialTenantCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A tenant is required.");
        return SocialResults.ToAction(this, await _social.CreateTenantAsync(command, ct));
    }

    [HttpPost("{tenantId:guid}/ai-terms")]
    public async Task<IActionResult> AcceptAiTerms(Guid tenantId, [FromBody] SocialAcceptAiTermsCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("The time of acceptance is required.");
        return SocialResults.ToAction(this, await _social.AcceptAiTermsAsync(tenantId, command.At, ct));
    }
}
