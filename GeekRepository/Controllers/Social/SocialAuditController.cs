using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Social;

[ApiController]
[Route("repo/social/audit")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class SocialAuditController : ControllerBase
{
    private readonly ISocialStore _social;

    public SocialAuditController(ISocialStore social) => _social = social;

    [HttpPost]
    public async Task<IActionResult> Record([FromBody] ActionAudit audit, CancellationToken ct)
    {
        if (audit is null) return BadRequest("An audit row is required.");
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.RecordAuditAsync(tenantId, audit, ct));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? accountId, [FromQuery] int limit = 0, CancellationToken ct = default)
    {
        if (!SocialResults.TryTenant(this, out var tenantId, out var refusal)) return refusal;
        return SocialResults.ToAction(this, await _social.ListAuditAsync(tenantId, accountId, limit, ct));
    }
}
