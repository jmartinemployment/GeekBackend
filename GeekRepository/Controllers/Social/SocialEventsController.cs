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

    [HttpGet("{eventId:guid}")]
    public async Task<IActionResult> Get(Guid eventId, CancellationToken ct) =>
        SocialResults.ToAction(this, await _social.GetEventAsync(eventId, ct));
}
