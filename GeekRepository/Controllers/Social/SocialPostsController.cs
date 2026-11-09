using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Social;

[ApiController]
[Route("repo/social/posts")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class SocialPostsController : ControllerBase
{
    private readonly ISocialStore _social;

    public SocialPostsController(ISocialStore social) => _social = social;

    [HttpPost]
    public async Task<IActionResult> Record([FromBody] RecordSocialPostCommand command, CancellationToken ct)
    {
        if (command is null) return BadRequest("A post is required.");
        return SocialResults.ToAction(this, await _social.RecordPostAsync(command, ct));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid accountId, [FromQuery] int limit = 0, CancellationToken ct = default)
    {
        if (accountId == Guid.Empty) return BadRequest("accountId is required.");
        return SocialResults.ToAction(this, await _social.ListPostsAsync(accountId, limit, ct));
    }
}
