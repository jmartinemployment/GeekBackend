using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/client-profiles")]
public class ClientProfilesApiController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ClientProfilesApiController> _logger;

    public ClientProfilesApiController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<ClientProfilesApiController> logger)
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
        var profile = await _repo.GetClientProfileByClientIdAsync(clientId, ct);
        if (profile is null)
            return NotFound();

        return Ok(profile);
    }

    [HttpPost]
    public async Task<ActionResult<ClientProfileDto>> Create([FromBody] CreateClientProfileCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating client profile for client {ClientId}",
            _currentUser.UserId, command.ClientId);

        var profile = await _repo.CreateClientProfileAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, profile);
    }
}

[ApiController]
[Route("api/content-writer/v3/client-profile-versions")]
public class ClientProfileVersionsApiController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ClientProfileVersionsApiController> _logger;

    public ClientProfileVersionsApiController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<ClientProfileVersionsApiController> logger)
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
    public async Task<ActionResult<IReadOnlyList<ClientProfileVersionDto>>> GetByProfileId([FromQuery] Guid profileId, CancellationToken ct)
    {
        if (profileId == Guid.Empty)
            return BadRequest("profileId is required");

        _logger.LogInformation("User {UserId} fetching client profile versions for profile {ProfileId}",
            _currentUser.UserId, profileId);

        var versions = await _repo.GetClientProfileVersionsByProfileIdAsync(profileId, ct);
        return Ok(versions);
    }

    [HttpPost]
    public async Task<ActionResult<ClientProfileVersionDto>> Create([FromBody] CreateClientProfileVersionCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating client profile version for profile {ProfileId}",
            _currentUser.UserId, command.ProfileId);

        var version = await _repo.CreateClientProfileVersionAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = version.Id }, version);
    }
}

[ApiController]
[Route("api/content-writer/v3/client-brand-voice-links")]
public class ClientBrandVoiceLinksApiController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ClientBrandVoiceLinksApiController> _logger;

    public ClientBrandVoiceLinksApiController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<ClientBrandVoiceLinksApiController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientBrandVoiceLinkDto>> GetById(Guid id, CancellationToken ct)
    {
        var link = await _repo.GetClientBrandVoiceLinkByIdAsync(id, ct);
        if (link is null)
            return NotFound();

        return Ok(link);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientBrandVoiceLinkDto>>> GetByProfileVersionId([FromQuery] Guid profileVersionId, CancellationToken ct)
    {
        if (profileVersionId == Guid.Empty)
            return BadRequest("profileVersionId is required");

        _logger.LogInformation("User {UserId} fetching client brand voice links for profile version {ProfileVersionId}",
            _currentUser.UserId, profileVersionId);

        var links = await _repo.GetClientBrandVoiceLinksByProfileVersionIdAsync(profileVersionId, ct);
        return Ok(links);
    }

    [HttpPost]
    public async Task<ActionResult<ClientBrandVoiceLinkDto>> Create([FromBody] CreateClientBrandVoiceLinkCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating client brand voice link for profile version {ProfileVersionId}",
            _currentUser.UserId, command.ProfileVersionId);

        var link = await _repo.CreateClientBrandVoiceLinkAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = link.Id }, link);
    }
}
