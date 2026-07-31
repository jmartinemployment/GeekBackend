using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekApplication.Models.ContentWriterV4;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing strategy brief API. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/strategy-briefs")]
public class GcwStrategyBriefsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly IContentGeneratorFactory _contentGeneratorFactory;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwStrategyBriefsController> _logger;

    public GcwStrategyBriefsController(
        HttpContentWriterV3Repository repo,
        IContentGeneratorFactory contentGeneratorFactory,
        ICurrentUserContext currentUser,
        ILogger<GcwStrategyBriefsController> logger)
    {
        _repo = repo;
        _contentGeneratorFactory = contentGeneratorFactory;
        _currentUser = currentUser;
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
    public async Task<ActionResult<IReadOnlyList<StrategyBriefDto>>> List([FromQuery] Guid campaignId, CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        var briefs = await _repo.GetStrategyBriefsByCampaignIdAsync(campaignId, ct);
        return Ok(briefs);
    }

    [HttpPost]
    public async Task<ActionResult<StrategyBriefDto>> Create([FromBody] CreateGcwStrategyBriefRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.CampaignId == Guid.Empty)
            return BadRequest("campaignId is required");
        if (string.IsNullOrWhiteSpace(request.AudienceProfile))
            return BadRequest("audienceProfile is required");
        if (string.IsNullOrWhiteSpace(request.BuyingStage))
            return BadRequest("buyingStage is required");
        if (string.IsNullOrWhiteSpace(request.Angle))
            return BadRequest("angle is required");
        if (string.IsNullOrWhiteSpace(request.CallToAction))
            return BadRequest("callToAction is required");

        // Empty GUID allowed when no pain point linked yet (no FK).
        var command = new CreateStrategyBriefCommand(
            request.CampaignId,
            request.PainPointId ?? Guid.Empty,
            request.AudienceProfile.Trim(),
            request.BuyingStage.Trim(),
            request.Angle.Trim(),
            request.CallToAction.Trim());

        _logger.LogInformation(
            "GCW user {UserId} creating strategy brief for campaign {CampaignId}",
            _currentUser.UserId,
            request.CampaignId);

        var brief = await _repo.CreateStrategyBriefAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = brief.Id }, brief);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StrategyBriefDto>> Update(Guid id, [FromBody] UpdateGcwStrategyBriefRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (string.IsNullOrWhiteSpace(request.AudienceProfile))
            return BadRequest("audienceProfile is required");
        if (string.IsNullOrWhiteSpace(request.BuyingStage))
            return BadRequest("buyingStage is required");
        if (string.IsNullOrWhiteSpace(request.Angle))
            return BadRequest("angle is required");
        if (string.IsNullOrWhiteSpace(request.CallToAction))
            return BadRequest("callToAction is required");

        var command = new UpdateStrategyBriefCommand(
            id,
            request.AudienceProfile.Trim(),
            request.BuyingStage.Trim(),
            request.Angle.Trim(),
            request.CallToAction.Trim());

        _logger.LogInformation(
            "GCW user {UserId} updating strategy brief {StrategyBriefId}",
            _currentUser.UserId,
            id);

        try
        {
            var brief = await _repo.UpdateStrategyBriefAsync(command, ct);
            return Ok(brief);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPatch("{id:guid}/approve")]
    public async Task<ActionResult<StrategyBriefDto>> Approve(Guid id, CancellationToken ct)
    {
        _logger.LogInformation(
            "GCW user {UserId} approving strategy brief {StrategyBriefId}",
            _currentUser.UserId,
            id);

        try
        {
            var brief = await _repo.ApproveStrategyBriefAsync(id, ct);
            return Ok(brief);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    [HttpPatch("{id:guid}/reject")]
    public async Task<ActionResult<StrategyBriefDto>> Reject(Guid id, CancellationToken ct)
    {
        _logger.LogInformation(
            "GCW user {UserId} rejecting strategy brief {StrategyBriefId}",
            _currentUser.UserId,
            id);

        try
        {
            var brief = await _repo.RejectStrategyBriefAsync(id, ct);
            return Ok(brief);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Brand-grounded structured draft: brief + pain/evidence + profile facts/voice → new asset version.
    /// Horizon B drafting excellence — not exposed under /api/content-writer/v3/*.
    /// </summary>
    [HttpPost("{id:guid}/generate")]
    public async Task<ActionResult<ContentAssetVersionDto>> Generate(
        Guid id,
        [FromBody] GenerateGcwDraftRequest request,
        CancellationToken ct)
    {
        if (request is null || request.AssetId == Guid.Empty)
            return BadRequest("assetId is required");

        if (!Enum.TryParse<ContentGeneratorProvider>(
                request.Provider ?? "OpenAi",
                ignoreCase: true,
                out var provider))
        {
            return BadRequest(
                $"Unknown provider '{request.Provider}'. Valid: {string.Join(", ", Enum.GetNames<ContentGeneratorProvider>())}.");
        }

        var brief = await _repo.GetStrategyBriefByIdAsync(id, ct);
        if (brief is null)
            return NotFound();

        var asset = await _repo.GetAssetByIdAsync(request.AssetId, ct);
        if (asset is null)
            return BadRequest("asset not found");
        if (asset.CampaignId != brief.CampaignId)
            return BadRequest("asset must belong to the brief's campaign");

        var campaign = await _repo.GetCampaignByIdAsync(brief.CampaignId, ct);
        var brandContext = await BuildBrandContextAsync(campaign, ct);

        PainPointDto? painPoint = null;
        var evidenceStatements = new List<string>();
        if (brief.PainPointId != Guid.Empty)
        {
            painPoint = await _repo.GetPainPointByIdAsync(brief.PainPointId, ct);
            var evidenceLinks = await _repo.GetPainPointEvidenceLinksByPainPointIdAsync(brief.PainPointId, ct);
            foreach (var link in evidenceLinks)
            {
                var evidence = await _repo.GetResearchEvidenceByIdAsync(link.ResearchEvidenceId, ct);
                if (evidence is not null && evidence.ApprovedForClaim)
                    evidenceStatements.Add(evidence.Statement);
            }
        }

        var audience = brief.AudienceProfile;
        if (painPoint is not null)
            audience = $"{audience} (pain point: {painPoint.Name} — {painPoint.ReaderSymptom})";
        if (!string.IsNullOrWhiteSpace(brandContext.AudienceSuffix))
            audience = $"{audience}\n\n{brandContext.AudienceSuffix}";

        if (brandContext.EvidenceExtras.Count > 0)
            evidenceStatements.AddRange(brandContext.EvidenceExtras);

        _logger.LogInformation(
            "GCW user {UserId} generating draft for brief {BriefId} → asset {AssetId} via {Provider} (brand={HasBrand})",
            _currentUser.UserId,
            id,
            request.AssetId,
            provider,
            brandContext.HasBrand);

        try
        {
            var generator = _contentGeneratorFactory.Get(provider);
            var bodyDocumentJson = await generator.GenerateStructuredDraftAsync(
                angle: brief.Angle,
                audienceProfile: audience,
                buyingStage: brief.BuyingStage,
                callToAction: brief.CallToAction,
                supportingEvidence: evidenceStatements,
                ct: ct);

            var version = await _repo.CreateAssetVersionAsync(
                new CreateContentAssetVersionCommand(request.AssetId, bodyDocumentJson),
                ct);
            return Ok(version);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Draft generation misconfigured for {Provider}", provider);
            return StatusCode(503, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Draft generation provider call failed");
            return StatusCode(502, "LLM provider request failed");
        }
    }

    private async Task<BrandContext> BuildBrandContextAsync(
        ContentCampaignDto? campaign,
        CancellationToken ct)
    {
        if (campaign is null || campaign.ProfileVersionId == Guid.Empty)
            return BrandContext.Empty;

        var version = await _repo.GetClientProfileVersionByIdAsync(campaign.ProfileVersionId, ct);
        if (version is null)
            return BrandContext.Empty;

        var parts = new List<string>();
        var evidence = new List<string>();

        if (version.ApprovedFacts.Count > 0)
        {
            var facts = JsonSerializer.Serialize(version.ApprovedFacts);
            parts.Add($"Approved brand facts (treat as ground truth): {facts}");
            evidence.Add($"Approved brand facts: {facts}");
        }

        if (version.ProhibitedClaims.Count > 0)
        {
            var banned = JsonSerializer.Serialize(version.ProhibitedClaims);
            parts.Add($"Prohibited claims (never assert these): {banned}");
        }

        var links = await _repo.GetClientBrandVoiceLinksByProfileVersionIdAsync(version.Id, ct);
        BrandVoiceDto? voice = null;
        foreach (var link in links)
        {
            voice = await _repo.GetBrandVoiceByIdAsync(link.BrandVoiceId, ct);
            if (voice is not null)
                break;
        }

        if (voice is not null)
        {
            parts.Add(
                $"Brand voice “{voice.Name}”: tone={voice.Tone}. " +
                $"Description: {voice.Description}. Sample: {voice.SampleText}");
            parts.Add("Match this voice consistently; do not drift into a generic marketing tone.");
        }

        if (parts.Count == 0)
            return BrandContext.Empty;

        return new BrandContext(
            true,
            "Brand Core constraints:\n- " + string.Join("\n- ", parts),
            evidence);
    }

    private sealed record BrandContext(
        bool HasBrand,
        string AudienceSuffix,
        List<string> EvidenceExtras)
    {
        public static BrandContext Empty { get; } = new(false, "", new List<string>());
    }

    public sealed record CreateGcwStrategyBriefRequest(
        Guid CampaignId,
        string AudienceProfile,
        string BuyingStage,
        string Angle,
        string CallToAction,
        Guid? PainPointId = null);

    public sealed record UpdateGcwStrategyBriefRequest(
        string AudienceProfile,
        string BuyingStage,
        string Angle,
        string CallToAction);

    public sealed record GenerateGcwDraftRequest(Guid AssetId, string? Provider = null);
}
