using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing pain point API. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/pain-points")]
public class GcwPainPointsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwPainPointsController> _logger;

    public GcwPainPointsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwPainPointsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PainPointDto>> GetById(Guid id, CancellationToken ct)
    {
        var painPoint = await _repo.GetPainPointByIdAsync(id, ct);
        if (painPoint is null)
            return NotFound();
        return Ok(painPoint);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PainPointDto>>> List(
        [FromQuery] Guid clientId,
        CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        var painPoints = await _repo.GetPainPointsByClientIdAsync(clientId, ct);
        return Ok(painPoints);
    }

    [HttpPost]
    public async Task<ActionResult<PainPointDto>> Create(
        [FromBody] CreateGcwPainPointRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.ClientId == Guid.Empty)
            return BadRequest("clientId is required");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("description is required");
        if (string.IsNullOrWhiteSpace(request.ReaderSymptom))
            return BadRequest("readerSymptom is required");
        if (string.IsNullOrWhiteSpace(request.CostOfInaction))
            return BadRequest("costOfInaction is required");
        if (string.IsNullOrWhiteSpace(request.OfferTerminology))
            return BadRequest("offerTerminology is required");

        var confidence = request.Confidence ?? 50;
        if (confidence is < 0 or > 100)
            return BadRequest("confidence must be 0–100");

        var objections = (request.Objections ?? [])
            .Select(o => o.Trim())
            .Where(o => o.Length > 0)
            .ToList();

        _logger.LogInformation(
            "GCW user {UserId} creating pain point for client {ClientId}",
            _currentUser.UserId,
            request.ClientId);

        var painPoint = await _repo.CreatePainPointAsync(
            new CreatePainPointCommand(
                request.ClientId,
                request.Name.Trim(),
                request.Description.Trim(),
                request.ReaderSymptom.Trim(),
                request.CostOfInaction.Trim(),
                request.OfferTerminology.Trim(),
                objections,
                confidence),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = painPoint.Id }, painPoint);
    }

    public sealed record CreateGcwPainPointRequest(
        Guid ClientId,
        string Name,
        string Description,
        string ReaderSymptom,
        string CostOfInaction,
        string OfferTerminology,
        List<string>? Objections = null,
        int? Confidence = null);
}
