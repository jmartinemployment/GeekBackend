using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing client profile version API. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/client-profile-versions")]
public class GcwClientProfileVersionsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwClientProfileVersionsController> _logger;

    public GcwClientProfileVersionsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwClientProfileVersionsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientProfileVersionDto>> GetById(Guid id, CancellationToken ct)
    {
        var version = await _repo.GetClientProfileVersionByIdAsync(id, ct);
        if (version is null)
            return NotFound();
        return Ok(version);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientProfileVersionDto>>> List(
        [FromQuery] Guid profileId,
        CancellationToken ct)
    {
        if (profileId == Guid.Empty)
            return BadRequest("profileId is required");

        var versions = await _repo.GetClientProfileVersionsByProfileIdAsync(profileId, ct);
        return Ok(versions);
    }

    [HttpPost]
    public async Task<ActionResult<ClientProfileVersionDto>> Create(
        [FromBody] CreateGcwClientProfileVersionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.ProfileId == Guid.Empty)
            return BadRequest("profileId is required");

        var approvedFacts = request.ApprovedFacts ?? new Dictionary<string, object>();
        var prohibitedClaims = request.ProhibitedClaims ?? new Dictionary<string, object>();

        _logger.LogInformation(
            "GCW user {UserId} creating client profile version for profile {ProfileId}",
            _currentUser.UserId,
            request.ProfileId);

        try
        {
            var version = await _repo.CreateClientProfileVersionAsync(
                new CreateClientProfileVersionCommand(
                    request.ProfileId,
                    approvedFacts,
                    prohibitedClaims),
                ct);
            return CreatedAtAction(nameof(GetById), new { id = version.Id }, version);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwClientProfileVersionRequest(
        Guid ProfileId,
        Dictionary<string, object>? ApprovedFacts = null,
        Dictionary<string, object>? ProhibitedClaims = null);
}
