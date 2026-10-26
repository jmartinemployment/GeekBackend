using GeekApplication.Models.Visitors;
using GeekRepository.Auth;
using GeekRepository.Repositories.Visitors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Visitors;

/// <summary>
/// Retention. Postgres has no TTL index, so GeekAPI's daily report job calls this once a day and
/// the store removes every session and visitor last seen more than 180 days before the given
/// instant. The schedule stays in GeekAPI; the rule and the delete stay here.
/// </summary>
[ApiController]
[Route("repo/visitors/retention")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class VisitorRetentionController : ControllerBase
{
    private readonly IVisitorRepository _visitors;

    public VisitorRetentionController(IVisitorRepository visitors) => _visitors = visitors;

    [HttpPost("purge")]
    public async Task<IActionResult> Purge([FromBody] VisitorPurgeCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("asOf is required.");
        return VisitorResults.ToAction(this, await _visitors.PurgeAsync(command, ct));
    }
}
