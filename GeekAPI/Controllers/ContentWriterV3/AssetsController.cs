using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/assets")]
public class AssetsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<AssetsController> _logger;

    public AssetsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<AssetsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentAssetDto>> GetById(Guid id, CancellationToken ct)
    {
        var asset = await _repo.GetAssetByIdAsync(id, ct);
        if (asset is null)
            return NotFound();

        return Ok(asset);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentAssetDto>>> GetByCampaignId(
        [FromQuery] Guid campaignId,
        CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        _logger.LogInformation("User {UserId} fetching assets for campaign {CampaignId}",
            _currentUser.UserId, campaignId);

        var assets = await _repo.GetAssetsByCampaignIdAsync(campaignId, ct);
        return Ok(assets);
    }

    [HttpPost]
    public async Task<ActionResult<ContentAssetDto>> Create([FromBody] CreateContentAssetCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating asset for campaign {CampaignId}",
            _currentUser.UserId, command.CampaignId);

        var asset = await _repo.CreateAssetAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = asset.Id }, asset);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} deleting asset {AssetId}",
            _currentUser.UserId, id);

        var success = await _repo.DeleteAssetAsync(id, ct);
        if (!success)
            return NotFound();

        return NoContent();
    }
}
