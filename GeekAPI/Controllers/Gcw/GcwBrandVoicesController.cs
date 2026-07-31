using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using GeekApplication.Models.ContentWriterV4;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing brand voices (CWV4 persistence) and profile-version links (CWV3).
/// Not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/brand-voices")]
public class GcwBrandVoicesController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwBrandVoicesController> _logger;

    public GcwBrandVoicesController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwBrandVoicesController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BrandVoiceDto>> GetById(Guid id, CancellationToken ct)
    {
        var voice = await _repo.GetBrandVoiceByIdAsync(id, ct);
        if (voice is null)
            return NotFound();
        if (voice.OwnerId != _currentUser.UserId)
            return Forbid();
        return Ok(voice);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BrandVoiceDto>>> List(CancellationToken ct)
    {
        var voices = await _repo.GetBrandVoicesByOwnerIdAsync(_currentUser.UserId, ct);
        return Ok(voices);
    }

    [HttpPost]
    public async Task<ActionResult<BrandVoiceDto>> Create(
        [FromBody] CreateGcwBrandVoiceRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");
        if (string.IsNullOrWhiteSpace(request.Tone))
            return BadRequest("tone is required");

        _logger.LogInformation(
            "GCW user {UserId} creating brand voice {Name}",
            _currentUser.UserId,
            request.Name);

        var voice = await _repo.CreateBrandVoiceAsync(
            new CreateBrandVoiceCommand(
                _currentUser.UserId,
                request.Name.Trim(),
                request.Description?.Trim() ?? "",
                request.Tone.Trim(),
                request.SampleText?.Trim() ?? ""),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = voice.Id }, voice);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BrandVoiceDto>> Update(
        Guid id,
        [FromBody] UpdateGcwBrandVoiceRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");

        var existing = await _repo.GetBrandVoiceByIdAsync(id, ct);
        if (existing is null)
            return NotFound();
        if (existing.OwnerId != _currentUser.UserId)
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");
        if (string.IsNullOrWhiteSpace(request.Tone))
            return BadRequest("tone is required");

        _logger.LogInformation(
            "GCW user {UserId} updating brand voice {VoiceId}",
            _currentUser.UserId,
            id);

        try
        {
            var voice = await _repo.UpdateBrandVoiceAsync(
                new UpdateBrandVoiceCommand(
                    id,
                    request.Name.Trim(),
                    request.Description?.Trim() ?? "",
                    request.Tone.Trim(),
                    request.SampleText?.Trim() ?? ""),
                ct);
            return Ok(voice);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwBrandVoiceRequest(
        string Name,
        string? Description,
        string Tone,
        string? SampleText);

    public sealed record UpdateGcwBrandVoiceRequest(
        string Name,
        string? Description,
        string Tone,
        string? SampleText);
}

[ApiController]
[Route("api/gcw/brand-voice-links")]
public class GcwBrandVoiceLinksController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwBrandVoiceLinksController> _logger;

    public GcwBrandVoiceLinksController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwBrandVoiceLinksController> logger)
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
    public async Task<ActionResult<IReadOnlyList<ClientBrandVoiceLinkDto>>> List(
        [FromQuery] Guid profileVersionId,
        CancellationToken ct)
    {
        if (profileVersionId == Guid.Empty)
            return BadRequest("profileVersionId is required");

        var links = await _repo.GetClientBrandVoiceLinksByProfileVersionIdAsync(profileVersionId, ct);
        return Ok(links);
    }

    [HttpPost]
    public async Task<ActionResult<ClientBrandVoiceLinkDto>> Create(
        [FromBody] CreateGcwBrandVoiceLinkRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.ProfileVersionId == Guid.Empty)
            return BadRequest("profileVersionId is required");
        if (request.BrandVoiceId == Guid.Empty)
            return BadRequest("brandVoiceId is required");

        var voice = await _repo.GetBrandVoiceByIdAsync(request.BrandVoiceId, ct);
        if (voice is null)
            return BadRequest("brandVoiceId not found");
        if (voice.OwnerId != _currentUser.UserId)
            return Forbid();

        _logger.LogInformation(
            "GCW user {UserId} linking brand voice {BrandVoiceId} to profile version {ProfileVersionId}",
            _currentUser.UserId,
            request.BrandVoiceId,
            request.ProfileVersionId);

        var link = await _repo.CreateClientBrandVoiceLinkAsync(
            new CreateClientBrandVoiceLinkCommand(request.ProfileVersionId, request.BrandVoiceId),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = link.Id }, link);
    }

    public sealed record CreateGcwBrandVoiceLinkRequest(Guid ProfileVersionId, Guid BrandVoiceId);
}
