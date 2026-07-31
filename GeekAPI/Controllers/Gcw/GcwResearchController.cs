using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing research APIs. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/research-runs")]
public class GcwResearchRunsController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "queued",
        "running",
        "completed",
        "failed",
        "completed-with-partial-coverage",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwResearchRunsController> _logger;

    public GcwResearchRunsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwResearchRunsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchRunDto>> GetById(Guid id, CancellationToken ct)
    {
        var run = await _repo.GetResearchRunByIdAsync(id, ct);
        if (run is null)
            return NotFound();
        return Ok(run);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResearchRunDto>>> List(
        [FromQuery] Guid campaignId,
        CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        var runs = await _repo.GetResearchRunsByCampaignIdAsync(campaignId, ct);
        return Ok(runs);
    }

    [HttpPost]
    public async Task<ActionResult<ResearchRunDto>> Create(
        [FromBody] CreateGcwResearchRunRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.CampaignId == Guid.Empty)
            return BadRequest("campaignId is required");
        if (string.IsNullOrWhiteSpace(request.Keyword))
            return BadRequest("keyword is required");

        var maxBudget = request.MaxBudget ?? 10m;
        if (maxBudget <= 0)
            return BadRequest("maxBudget must be positive");

        _logger.LogInformation(
            "GCW user {UserId} creating research run for campaign {CampaignId}",
            _currentUser.UserId,
            request.CampaignId);

        var run = await _repo.CreateResearchRunAsync(
            new CreateResearchRunCommand(
                request.CampaignId,
                request.Keyword.Trim(),
                maxBudget),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = run.Id }, run);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ResearchRunDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateGcwResearchRunStatusRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("status is required");

        var status = request.Status.Trim();
        if (!AllowedStatuses.Contains(status))
            return BadRequest($"status must be one of: {string.Join(", ", AllowedStatuses)}");

        var existing = await _repo.GetResearchRunByIdAsync(id, ct);
        if (existing is null)
            return NotFound();

        var discovered = request.DiscoveredSourceCount ?? existing.DiscoveredSourceCount;
        var spent = request.SpentBudget ?? existing.SpentBudget;

        _logger.LogInformation(
            "GCW user {UserId} updating research run {RunId} to {Status}",
            _currentUser.UserId,
            id,
            status);

        var run = await _repo.UpdateResearchRunStatusAsync(
            id,
            status,
            discovered,
            spent,
            request.ErrorMessage,
            ct);
        return Ok(run);
    }

    public sealed record CreateGcwResearchRunRequest(
        Guid CampaignId,
        string Keyword,
        decimal? MaxBudget = null);

    public sealed record UpdateGcwResearchRunStatusRequest(
        string Status,
        int? DiscoveredSourceCount = null,
        decimal? SpentBudget = null,
        string? ErrorMessage = null);
}

[ApiController]
[Route("api/gcw/research-sources")]
public class GcwResearchSourcesController : ControllerBase
{
    private static readonly HashSet<string> AllowedSourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ExistingInternal",
        "OperatorUploaded",
        "AgentDiscoveredExternal",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwResearchSourcesController> _logger;

    public GcwResearchSourcesController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwResearchSourcesController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchSourceDto>> GetById(Guid id, CancellationToken ct)
    {
        var source = await _repo.GetResearchSourceByIdAsync(id, ct);
        if (source is null)
            return NotFound();
        return Ok(source);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResearchSourceDto>>> List(
        [FromQuery] Guid researchRunId,
        CancellationToken ct)
    {
        if (researchRunId == Guid.Empty)
            return BadRequest("researchRunId is required");

        var sources = await _repo.GetResearchSourcesByRunIdAsync(researchRunId, ct);
        return Ok(sources);
    }

    [HttpPost]
    public async Task<ActionResult<ResearchSourceDto>> Create(
        [FromBody] CreateGcwResearchSourceRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.ResearchRunId == Guid.Empty)
            return BadRequest("researchRunId is required");
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("title is required");
        if (string.IsNullOrWhiteSpace(request.SourceType))
            return BadRequest("sourceType is required");

        var sourceType = request.SourceType.Trim();
        if (!AllowedSourceTypes.Contains(sourceType))
            return BadRequest($"sourceType must be one of: {string.Join(", ", AllowedSourceTypes)}");

        sourceType = AllowedSourceTypes.First(s =>
            s.Equals(sourceType, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "GCW user {UserId} creating research source for run {RunId}",
            _currentUser.UserId,
            request.ResearchRunId);

        var source = await _repo.CreateResearchSourceAsync(
            new CreateResearchSourceCommand(
                request.ResearchRunId,
                sourceType,
                string.IsNullOrWhiteSpace(request.Url) ? null : request.Url.Trim(),
                request.Title.Trim(),
                string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = source.Id }, source);
    }

    public sealed record CreateGcwResearchSourceRequest(
        Guid ResearchRunId,
        string SourceType,
        string Title,
        string? Url = null,
        string? Description = null);
}

[ApiController]
[Route("api/gcw/research-evidence")]
public class GcwResearchEvidenceController : ControllerBase
{
    private static readonly HashSet<string> AllowedSupportLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "VerifiedClientFact",
        "VerifiedExternalSource",
        "ObservedMarketLanguage",
        "Unsupported",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwResearchEvidenceController> _logger;

    public GcwResearchEvidenceController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwResearchEvidenceController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResearchEvidenceDto>> GetById(Guid id, CancellationToken ct)
    {
        var evidence = await _repo.GetResearchEvidenceByIdAsync(id, ct);
        if (evidence is null)
            return NotFound();
        return Ok(evidence);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResearchEvidenceDto>>> List(
        [FromQuery] Guid sourceId,
        CancellationToken ct)
    {
        if (sourceId == Guid.Empty)
            return BadRequest("sourceId is required");

        var evidence = await _repo.GetResearchEvidenceBySourceIdAsync(sourceId, ct);
        return Ok(evidence);
    }

    [HttpPost]
    public async Task<ActionResult<ResearchEvidenceDto>> Create(
        [FromBody] CreateGcwResearchEvidenceRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.ResearchSourceId == Guid.Empty)
            return BadRequest("researchSourceId is required");
        if (string.IsNullOrWhiteSpace(request.Statement))
            return BadRequest("statement is required");
        if (string.IsNullOrWhiteSpace(request.SupportLevel))
            return BadRequest("supportLevel is required");

        var supportLevel = request.SupportLevel.Trim();
        if (!AllowedSupportLevels.Contains(supportLevel))
            return BadRequest($"supportLevel must be one of: {string.Join(", ", AllowedSupportLevels)}");

        var confidence = request.Confidence ?? 50;
        if (confidence is < 0 or > 100)
            return BadRequest("confidence must be 0–100");

        // Normalize casing to canonical values
        supportLevel = AllowedSupportLevels.First(s =>
            s.Equals(supportLevel, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "GCW user {UserId} creating research evidence for source {SourceId}",
            _currentUser.UserId,
            request.ResearchSourceId);

        var evidence = await _repo.CreateResearchEvidenceAsync(
            new CreateResearchEvidenceCommand(
                request.ResearchSourceId,
                request.Statement.Trim(),
                supportLevel,
                confidence),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = evidence.Id }, evidence);
    }

    [HttpPatch("{id:guid}/approve")]
    public async Task<ActionResult<ResearchEvidenceDto>> Approve(Guid id, CancellationToken ct)
    {
        _logger.LogInformation(
            "GCW user {UserId} approving research evidence {EvidenceId}",
            _currentUser.UserId,
            id);

        var evidence = await _repo.ApproveResearchEvidenceAsync(id, ct);
        return Ok(evidence);
    }

    public sealed record CreateGcwResearchEvidenceRequest(
        Guid ResearchSourceId,
        string Statement,
        string SupportLevel,
        int? Confidence = null);
}
