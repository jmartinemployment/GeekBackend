using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/client-profiles")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ClientProfilesController : ControllerBase
{
    private readonly IClientProfileRepository _repository;

    public ClientProfilesController(IClientProfileRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientProfileDto>> GetById(Guid id, CancellationToken ct)
    {
        var profile = await _repository.GetByIdAsync(id, ct);
        if (profile is null)
            return NotFound();

        return Ok(profile);
    }

    [HttpGet("by-client/{clientId:guid}")]
    public async Task<ActionResult<ClientProfileDto>> GetByClientId(Guid clientId, CancellationToken ct)
    {
        var profile = await _repository.GetByClientIdAsync(clientId, ct);
        if (profile is null)
            return NotFound();

        return Ok(profile);
    }

    [HttpPost]
    public async Task<ActionResult<ClientProfileDto>> Create([FromBody] CreateClientProfileCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var profile = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, profile);
    }
}

[ApiController]
[Route("repo/content-writer-v3/client-profile-versions")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ClientProfileVersionsController : ControllerBase
{
    private readonly IClientProfileVersionRepository _repository;

    public ClientProfileVersionsController(IClientProfileVersionRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientProfileVersionDto>> GetById(Guid id, CancellationToken ct)
    {
        var version = await _repository.GetByIdAsync(id, ct);
        if (version is null)
            return NotFound();

        return Ok(version);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientProfileVersionDto>>> GetByProfileId([FromQuery] Guid profileId, CancellationToken ct)
    {
        if (profileId == Guid.Empty)
            return BadRequest("profileId is required");

        var versions = await _repository.GetByProfileIdAsync(profileId, ct);
        return Ok(versions);
    }

    [HttpPost]
    public async Task<ActionResult<ClientProfileVersionDto>> Create([FromBody] CreateClientProfileVersionCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var version = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = version.Id }, version);
    }
}

[ApiController]
[Route("repo/content-writer-v3/client-brand-voice-links")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ClientBrandVoiceLinksController : ControllerBase
{
    private readonly IClientBrandVoiceLinkRepository _repository;

    public ClientBrandVoiceLinksController(IClientBrandVoiceLinkRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClientBrandVoiceLinkDto>> GetById(Guid id, CancellationToken ct)
    {
        var link = await _repository.GetByIdAsync(id, ct);
        if (link is null)
            return NotFound();

        return Ok(link);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientBrandVoiceLinkDto>>> GetByProfileVersionId([FromQuery] Guid profileVersionId, CancellationToken ct)
    {
        if (profileVersionId == Guid.Empty)
            return BadRequest("profileVersionId is required");

        var links = await _repository.GetByProfileVersionIdAsync(profileVersionId, ct);
        return Ok(links);
    }

    [HttpPost]
    public async Task<ActionResult<ClientBrandVoiceLinkDto>> Create([FromBody] CreateClientBrandVoiceLinkCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var link = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = link.Id }, link);
    }
}
