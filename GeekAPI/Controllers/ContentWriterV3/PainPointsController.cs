using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/pain-points")]
public class PainPointsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ILogger<PainPointsController> _logger;

    public PainPointsController(HttpContentWriterV3Repository repo, ILogger<PainPointsController> logger)
    {
        _repo = repo;
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
    public async Task<ActionResult<IReadOnlyList<PainPointDto>>> GetByClientId([FromQuery] Guid clientId, CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required");

        var painPoints = await _repo.GetPainPointsByClientIdAsync(clientId, ct);
        return Ok(painPoints);
    }

    [HttpPost]
    public async Task<ActionResult<PainPointDto>> Create([FromBody] CreatePainPointCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Creating pain point for client {ClientId}", command.ClientId);
        var painPoint = await _repo.CreatePainPointAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = painPoint.Id }, painPoint);
    }
}

[ApiController]
[Route("api/content-writer/v3/strategy-briefs")]
public class StrategyBriefsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ILogger<StrategyBriefsController> _logger;

    public StrategyBriefsController(HttpContentWriterV3Repository repo, ILogger<StrategyBriefsController> logger)
    {
        _repo = repo;
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
    public async Task<ActionResult<IReadOnlyList<StrategyBriefDto>>> GetByCampaignId([FromQuery] Guid campaignId, CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        var briefs = await _repo.GetStrategyBriefsByCampaignIdAsync(campaignId, ct);
        return Ok(briefs);
    }

    [HttpPost]
    public async Task<ActionResult<StrategyBriefDto>> Create([FromBody] CreateStrategyBriefCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Creating strategy brief for campaign {CampaignId}", command.CampaignId);
        var brief = await _repo.CreateStrategyBriefAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = brief.Id }, brief);
    }

    [HttpPatch("{id:guid}/approve")]
    public async Task<ActionResult<StrategyBriefDto>> Approve(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Approving strategy brief {StrategyBriefId}", id);
        var brief = await _repo.ApproveStrategyBriefAsync(id, ct);
        return Ok(brief);
    }

    [HttpPatch("{id:guid}/reject")]
    public async Task<ActionResult<StrategyBriefDto>> Reject(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Rejecting strategy brief {StrategyBriefId}", id);
        var brief = await _repo.RejectStrategyBriefAsync(id, ct);
        return Ok(brief);
    }
}

[ApiController]
[Route("api/content-writer/v3/publications")]
public class PublicationsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ILogger<PublicationsController> _logger;

    public PublicationsController(HttpContentWriterV3Repository repo, ILogger<PublicationsController> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PublicationDto>> GetById(Guid id, CancellationToken ct)
    {
        var publication = await _repo.GetPublicationByIdAsync(id, ct);
        if (publication is null)
            return NotFound();
        return Ok(publication);
    }

    [HttpPost]
    public async Task<ActionResult<PublicationDto>> Create([FromBody] CreatePublicationCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Creating publication for asset version {AssetVersionId}", command.AssetVersionId);
        var publication = await _repo.CreatePublicationAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = publication.Id }, publication);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<PublicationDto>> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Updating publication {PublicationId} status to {Status}", id, request.Status);
        var publication = await _repo.UpdatePublicationStatusAsync(id, request.Status, ct);
        return Ok(publication);
    }

    public record UpdateStatusRequest(string Status);
}
