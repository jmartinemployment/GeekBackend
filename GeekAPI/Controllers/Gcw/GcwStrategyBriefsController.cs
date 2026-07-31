using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing strategy brief API. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/strategy-briefs")]
public class GcwStrategyBriefsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwStrategyBriefsController> _logger;

    public GcwStrategyBriefsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwStrategyBriefsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StrategyBriefDto>> GetById(Guid id, CancellationToken ct)
    {
        var brief = await _repo.GetStrategyBriefByIdAsync(id, ct);
        if (brief is null)
            return NotFound();
        return Ok(brief);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StrategyBriefDto>>> List([FromQuery] Guid campaignId, CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        var briefs = await _repo.GetStrategyBriefsByCampaignIdAsync(campaignId, ct);
        return Ok(briefs);
    }

    [HttpPost]
    public async Task<ActionResult<StrategyBriefDto>> Create([FromBody] CreateGcwStrategyBriefRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.CampaignId == Guid.Empty)
            return BadRequest("campaignId is required");
        if (string.IsNullOrWhiteSpace(request.AudienceProfile))
            return BadRequest("audienceProfile is required");
        if (string.IsNullOrWhiteSpace(request.BuyingStage))
            return BadRequest("buyingStage is required");
        if (string.IsNullOrWhiteSpace(request.Angle))
            return BadRequest("angle is required");
        if (string.IsNullOrWhiteSpace(request.CallToAction))
            return BadRequest("callToAction is required");

        // Pain points land in P2 — empty GUID is allowed (no FK).
        var command = new CreateStrategyBriefCommand(
            request.CampaignId,
            request.PainPointId ?? Guid.Empty,
            request.AudienceProfile.Trim(),
            request.BuyingStage.Trim(),
            request.Angle.Trim(),
            request.CallToAction.Trim());

        _logger.LogInformation(
            "GCW user {UserId} creating strategy brief for campaign {CampaignId}",
            _currentUser.UserId,
            request.CampaignId);

        var brief = await _repo.CreateStrategyBriefAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = brief.Id }, brief);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StrategyBriefDto>> Update(Guid id, [FromBody] UpdateGcwStrategyBriefRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (string.IsNullOrWhiteSpace(request.AudienceProfile))
            return BadRequest("audienceProfile is required");
        if (string.IsNullOrWhiteSpace(request.BuyingStage))
            return BadRequest("buyingStage is required");
        if (string.IsNullOrWhiteSpace(request.Angle))
            return BadRequest("angle is required");
        if (string.IsNullOrWhiteSpace(request.CallToAction))
            return BadRequest("callToAction is required");

        var command = new UpdateStrategyBriefCommand(
            id,
            request.AudienceProfile.Trim(),
            request.BuyingStage.Trim(),
            request.Angle.Trim(),
            request.CallToAction.Trim());

        _logger.LogInformation(
            "GCW user {UserId} updating strategy brief {StrategyBriefId}",
            _currentUser.UserId,
            id);

        try
        {
            var brief = await _repo.UpdateStrategyBriefAsync(command, ct);
            return Ok(brief);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPatch("{id:guid}/approve")]
    public async Task<ActionResult<StrategyBriefDto>> Approve(Guid id, CancellationToken ct)
    {
        _logger.LogInformation(
            "GCW user {UserId} approving strategy brief {StrategyBriefId}",
            _currentUser.UserId,
            id);

        try
        {
            var brief = await _repo.ApproveStrategyBriefAsync(id, ct);
            return Ok(brief);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    [HttpPatch("{id:guid}/reject")]
    public async Task<ActionResult<StrategyBriefDto>> Reject(Guid id, CancellationToken ct)
    {
        _logger.LogInformation(
            "GCW user {UserId} rejecting strategy brief {StrategyBriefId}",
            _currentUser.UserId,
            id);

        try
        {
            var brief = await _repo.RejectStrategyBriefAsync(id, ct);
            return Ok(brief);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwStrategyBriefRequest(
        Guid CampaignId,
        string AudienceProfile,
        string BuyingStage,
        string Angle,
        string CallToAction,
        Guid? PainPointId = null);

    public sealed record UpdateGcwStrategyBriefRequest(
        string AudienceProfile,
        string BuyingStage,
        string Angle,
        string CallToAction);
}
