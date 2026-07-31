using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing client profile API. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/client-profiles")]
public class GcwClientProfilesController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwClientProfilesController> _logger;

    public GcwClientProfilesController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwClientProfilesController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientProfileDto>> GetById(Guid id, CancellationToken ct)
    {
        var profile = await _repo.GetClientProfileByIdAsync(id, ct);
        if (profile is null)
            return NotFound();
        return Ok(profile);
    }

    [HttpGet("by-client/{clientId:guid}")]
    public async Task<ActionResult<ClientProfileDto>> GetByClientId(Guid clientId, CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        var profile = await _repo.GetClientProfileByClientIdAsync(clientId, ct);
        if (profile is null)
            return NotFound();
        return Ok(profile);
    }

    [HttpPost]
    public async Task<ActionResult<ClientProfileDto>> Create([FromBody] CreateGcwClientProfileRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.ClientId == Guid.Empty)
            return BadRequest("clientId is required");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");

        var existing = await _repo.GetClientProfileByClientIdAsync(request.ClientId, ct);
        if (existing is not null)
            return Conflict(existing);

        _logger.LogInformation(
            "GCW user {UserId} creating client profile for client {ClientId}",
            _currentUser.UserId,
            request.ClientId);

        var profile = await _repo.CreateClientProfileAsync(
            new CreateClientProfileCommand(request.ClientId, request.Name.Trim()),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, profile);
    }

    public sealed record CreateGcwClientProfileRequest(Guid ClientId, string Name);
}
