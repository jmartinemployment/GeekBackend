using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Social;

/// <summary>
/// The two calls a caller makes on a schedule or at startup. Postgres has no TTL, so the retention
/// purge is a call, and a send interrupted by a restart is failed by a call; the store never
/// decides on its own when either runs.
/// </summary>
[ApiController]
[Route("repo/social/maintenance")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class SocialMaintenanceController : ControllerBase
{
    private readonly ISocialStore _social;

    public SocialMaintenanceController(ISocialStore social) => _social = social;

    [HttpPost("purge")]
    public async Task<IActionResult> Purge([FromBody] SocialPurgeCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A purge request is required.");
        return SocialResults.ToAction(this, await _social.PurgeExpiredAsync(command.AsOf, ct));
    }

    [HttpPost("fail-interrupted-sends")]
    public async Task<IActionResult> FailInterruptedSends([FromBody] SocialFailInterruptedSendsCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A request is required.");
        return SocialResults.ToAction(this, await _social.FailInterruptedSendsAsync(command.At, ct));
    }
}
