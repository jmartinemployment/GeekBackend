using GeekAPI.HttpClients;
using GeekApplication.Interfaces.ContentWriterV3;
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
    private readonly IContentGenerator _contentGenerator;
    private readonly ILogger<StrategyBriefsController> _logger;

    public StrategyBriefsController(
        HttpContentWriterV3Repository repo,
        IContentGenerator contentGenerator,
        ILogger<StrategyBriefsController> logger)
    {
        _repo = repo;
        _contentGenerator = contentGenerator;
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

    public record GenerateDraftRequest(Guid AssetId);

    /// <summary>
    /// Generates a real ContentAssetVersion from this brief: loads the brief's linked pain point
    /// and research evidence, calls the LLM for structured JSON matching the frontend's
    /// ContentDocument schema, and persists it as a new version of the given asset. First real
    /// caller of IContentGenerator anywhere in this app — everything before this endpoint was
    /// CRUD-only, with generation registered in DI but never invoked.
    /// </summary>
    [HttpPost("{id:guid}/generate")]
    public async Task<ActionResult<ContentAssetVersionDto>> Generate(Guid id, [FromBody] GenerateDraftRequest request, CancellationToken ct)
    {
        var brief = await _repo.GetStrategyBriefByIdAsync(id, ct);
        if (brief is null)
        {
            return NotFound($"Strategy brief {id} not found.");
        }

        var asset = await _repo.GetAssetByIdAsync(request.AssetId, ct);
        if (asset is null)
        {
            return BadRequest($"Asset {request.AssetId} not found.");
        }

        var painPoint = await _repo.GetPainPointByIdAsync(brief.PainPointId, ct);

        var evidenceLinks = await _repo.GetPainPointEvidenceLinksByPainPointIdAsync(brief.PainPointId, ct);
        var evidenceStatements = new List<string>();
        foreach (var link in evidenceLinks)
        {
            var evidence = await _repo.GetResearchEvidenceByIdAsync(link.ResearchEvidenceId, ct);
            if (evidence is not null && evidence.ApprovedForClaim)
            {
                evidenceStatements.Add(evidence.Statement);
            }
        }

        _logger.LogInformation(
            "Generating structured draft for strategy brief {StrategyBriefId} (asset {AssetId}), {EvidenceCount} approved evidence statement(s)",
            id, request.AssetId, evidenceStatements.Count);

        var bodyDocumentJson = await _contentGenerator.GenerateStructuredDraftAsync(
            angle: brief.Angle,
            audienceProfile: painPoint is not null
                ? $"{brief.AudienceProfile} (pain point: {painPoint.Name} — {painPoint.ReaderSymptom})"
                : brief.AudienceProfile,
            buyingStage: brief.BuyingStage,
            callToAction: brief.CallToAction,
            supportingEvidence: evidenceStatements,
            ct: ct);

        var version = await _repo.CreateAssetVersionAsync(
            new CreateContentAssetVersionCommand(request.AssetId, bodyDocumentJson), ct);

        return Ok(version);
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
