using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/drafts")]
public class DraftsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<DraftsController> _logger;

    public DraftsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<DraftsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentAssetVersionDto>> GetById(Guid id, CancellationToken ct)
    {
        var version = await _repo.GetAssetVersionByIdAsync(id, ct);
        if (version is null)
            return NotFound();

        return Ok(version);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentAssetVersionDto>>> GetByAssetId([FromQuery] Guid assetId, CancellationToken ct)
    {
        if (assetId == Guid.Empty)
            return BadRequest("assetId is required");

        _logger.LogInformation("User {UserId} fetching drafts for asset {AssetId}",
            _currentUser.UserId, assetId);

        var versions = await _repo.GetAssetVersionsByAssetIdAsync(assetId, ct);
        return Ok(versions);
    }

    [HttpPost]
    public async Task<ActionResult<ContentAssetVersionDto>> Create([FromBody] CreateContentAssetVersionCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating draft for asset {AssetId}",
            _currentUser.UserId, command.AssetId);

        var version = await _repo.CreateAssetVersionAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = version.Id }, version);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ContentAssetVersionDto>> Update(Guid id, [FromBody] UpdateAssetVersionRequest request, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} updating draft {VersionId}",
            _currentUser.UserId, id);

        var version = await _repo.UpdateAssetVersionAsync(
            new UpdateContentAssetVersionCommand(id, request.BodyDocumentJson), ct);
        return Ok(version);
    }

    public record UpdateAssetVersionRequest(string BodyDocumentJson);
}
