using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Social;

/// <summary>
/// What a tenant's status page reads, and where GeekAPI keeps a step it could not complete. A failure is recorded for
/// the tenant the X-Social-Tenant-Id header names, or for no one tenant when the call carries no header; the status
/// read always needs the header and counts only that tenant's rows.
/// </summary>
[ApiController]
[Route("repo/social/status")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class SocialStatusController : ControllerBase
{
    private readonly ISocialStore _social;

    public SocialStatusController(ISocialStore social) => _social = social;

    [HttpPost("failures")]
    public async Task<IActionResult> RecordFailure([FromBody] RecordSocialStepFailureCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A step failure is required.");

        // The tenant comes from the header like every other call, never from the body, so a body cannot name a tenant
        // the caller did not send. No header is a failure that belongs to no one tenant; a header that is not a tenant
        // id is refused, not read as "no tenant".
        Guid? tenantId = null;
        if (Request.Headers.ContainsKey(SocialResults.TenantHeader))
        {
            if (!SocialResults.TryTenant(this, out var tenant, out var refusal)) return refusal;
            tenantId = tenant;
        }

        command.TenantId = tenantId;
        return SocialResults.ToAction(this, await _social.RecordStepFailureAsync(command, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Status([FromQuery] int failureLimit = 0, CancellationToken ct = default)
    {
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.GetTenantStatusAsync(tenantId, failureLimit, ct));
    }
}
