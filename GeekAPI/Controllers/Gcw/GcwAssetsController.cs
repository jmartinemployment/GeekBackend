using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.Gcw;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing content assets. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/assets")]
public class GcwAssetsController : ControllerBase
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "pillar",
        "companion",
    };

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft",
        "readyForApproval",
        "approved",
        "published",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwAssetsController> _logger;

    public GcwAssetsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwAssetsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentAssetDto>> GetById(Guid id, CancellationToken ct)
    {
        var asset = await _repo.GetAssetByIdAsync(id, ct);
        if (asset is null)
            return NotFound();
        return Ok(asset);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentAssetDto>>> List(
        [FromQuery] Guid campaignId,
        CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        var assets = await _repo.GetAssetsByCampaignIdAsync(campaignId, ct);
        return Ok(assets);
    }

    [HttpPost]
    public async Task<ActionResult<ContentAssetDto>> Create(
        [FromBody] CreateGcwAssetRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.CampaignId == Guid.Empty)
            return BadRequest("campaignId is required");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");
        if (string.IsNullOrWhiteSpace(request.Type))
            return BadRequest("type is required");

        var type = request.Type.Trim();
        if (!AllowedTypes.Contains(type))
            return BadRequest($"type must be one of: {string.Join(", ", AllowedTypes)}");
        type = AllowedTypes.First(t => t.Equals(type, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "GCW user {UserId} creating asset for campaign {CampaignId}",
            _currentUser.UserId,
            request.CampaignId);

        var asset = await _repo.CreateAssetAsync(
            new CreateContentAssetCommand(request.CampaignId, type, request.Name.Trim()),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = asset.Id }, asset);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ContentAssetDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateGcwAssetStatusRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("status is required");

        var status = request.Status.Trim();
        if (!AllowedStatuses.Contains(status))
            return BadRequest($"status must be one of: {string.Join(", ", AllowedStatuses)}");
        status = AllowedStatuses.First(s => s.Equals(status, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "GCW user {UserId} updating asset {AssetId} status to {Status}",
            _currentUser.UserId,
            id,
            status);

        try
        {
            var asset = await _repo.UpdateAssetStatusAsync(id, status, ct);
            return Ok(asset);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwAssetRequest(Guid CampaignId, string Type, string Name);
    public sealed record UpdateGcwAssetStatusRequest(string Status);
}

[ApiController]
[Route("api/gcw/asset-versions")]
public class GcwAssetVersionsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly IContentGeneratorFactory _contentGeneratorFactory;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwAssetVersionsController> _logger;

    public GcwAssetVersionsController(
        HttpContentWriterV3Repository repo,
        IContentGeneratorFactory contentGeneratorFactory,
        ICurrentUserContext currentUser,
        ILogger<GcwAssetVersionsController> logger)
    {
        _repo = repo;
        _contentGeneratorFactory = contentGeneratorFactory;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentAssetVersionDto>> GetById(Guid id, CancellationToken ct)
    {
        var version = await _repo.GetAssetVersionByIdAsync(id, ct);
        if (version is null)
            return NotFound();
        return Ok(version);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentAssetVersionDto>>> List(
        [FromQuery] Guid assetId,
        CancellationToken ct)
    {
        if (assetId == Guid.Empty)
            return BadRequest("assetId is required");

        var versions = await _repo.GetAssetVersionsByAssetIdAsync(assetId, ct);
        return Ok(versions);
    }

    [HttpPost]
    public async Task<ActionResult<ContentAssetVersionDto>> Create(
        [FromBody] CreateGcwAssetVersionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.AssetId == Guid.Empty)
            return BadRequest("assetId is required");
        if (string.IsNullOrWhiteSpace(request.BodyDocumentJson))
            return BadRequest("bodyDocumentJson is required");

        // Validate JSON shape
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(request.BodyDocumentJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest("bodyDocumentJson must be valid JSON");
        }

        _logger.LogInformation(
            "GCW user {UserId} creating asset version for asset {AssetId}",
            _currentUser.UserId,
            request.AssetId);

        var version = await _repo.CreateAssetVersionAsync(
            new CreateContentAssetVersionCommand(request.AssetId, request.BodyDocumentJson.Trim()),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = version.Id }, version);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContentAssetVersionDto>> Update(
        Guid id,
        [FromBody] UpdateGcwAssetVersionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (string.IsNullOrWhiteSpace(request.BodyDocumentJson))
            return BadRequest("bodyDocumentJson is required");

        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(request.BodyDocumentJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest("bodyDocumentJson must be valid JSON");
        }

        _logger.LogInformation(
            "GCW user {UserId} updating asset version {VersionId}",
            _currentUser.UserId,
            id);

        try
        {
            var version = await _repo.UpdateAssetVersionAsync(
                new UpdateContentAssetVersionCommand(id, request.BodyDocumentJson.Trim()),
                ct);
            return Ok(version);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// In-editor SEO report for a version against its campaign keyword.
    /// </summary>
    [HttpGet("{id:guid}/seo")]
    public async Task<ActionResult<GcwSeoAnalyzer.SeoReport>> GetSeo(Guid id, CancellationToken ct)
    {
        var version = await _repo.GetAssetVersionByIdAsync(id, ct);
        if (version is null)
            return NotFound();

        var asset = await _repo.GetAssetByIdAsync(version.AssetId, ct);
        if (asset is null)
            return NotFound();

        var campaign = await _repo.GetCampaignByIdAsync(asset.CampaignId, ct);
        var keyword = campaign?.Keyword ?? "";
        var report = GcwSeoAnalyzer.Analyze(version.BodyDocumentJson ?? "", keyword);
        return Ok(report);
    }

    /// <summary>
    /// Grammarly-class polish / ship-check for a version (clarity + prohibited claims).
    /// </summary>
    [HttpGet("{id:guid}/polish")]
    public async Task<ActionResult<GcwPolishAnalyzer.PolishReport>> GetPolish(Guid id, CancellationToken ct)
    {
        var version = await _repo.GetAssetVersionByIdAsync(id, ct);
        if (version is null)
            return NotFound();

        var asset = await _repo.GetAssetByIdAsync(version.AssetId, ct);
        if (asset is null)
            return NotFound();

        Dictionary<string, object>? prohibited = null;
        var campaign = await _repo.GetCampaignByIdAsync(asset.CampaignId, ct);
        if (campaign is not null && campaign.ProfileVersionId != Guid.Empty)
        {
            var profileVersion = await _repo.GetClientProfileVersionByIdAsync(campaign.ProfileVersionId, ct);
            prohibited = profileVersion?.ProhibitedClaims;
        }

        var phrases = GcwPolishAnalyzer.ExtractClaimPhrases(prohibited);
        var report = GcwPolishAnalyzer.Analyze(version.BodyDocumentJson ?? "", phrases);
        return Ok(report);
    }

    /// <summary>
    /// Iterative revise chat: apply feedback to a version's ContentDocument and save a new version.
    /// </summary>
    [HttpPost("{id:guid}/revise")]
    public async Task<ActionResult<ContentAssetVersionDto>> Revise(
        Guid id,
        [FromBody] ReviseGcwAssetVersionRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Feedback))
            return BadRequest("feedback is required");

        if (!Enum.TryParse<ContentGeneratorProvider>(
                request.Provider ?? "OpenAi",
                ignoreCase: true,
                out var provider))
        {
            return BadRequest(
                $"Unknown provider '{request.Provider}'. Valid: {string.Join(", ", Enum.GetNames<ContentGeneratorProvider>())}.");
        }

        if (request.Tone is not null && GcwDraftingCatalog.FindTone(request.Tone) is null)
            return BadRequest($"Unknown tone '{request.Tone}'");

        var current = await _repo.GetAssetVersionByIdAsync(id, ct);
        if (current is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(current.BodyDocumentJson))
            return BadRequest("version has no body document to revise");

        var feedback = request.Feedback.Trim();
        var draftingSuffix = GcwDraftingCatalog.BuildPromptSuffix(null, request.Tone);
        if (!string.IsNullOrWhiteSpace(draftingSuffix))
            feedback = $"{feedback}\n\n{draftingSuffix}";

        _logger.LogInformation(
            "GCW user {UserId} revising asset version {VersionId} via {Provider} (tone={Tone})",
            _currentUser.UserId,
            id,
            provider,
            request.Tone);

        try
        {
            var generator = _contentGeneratorFactory.Get(provider);
            var revisedJson = await generator.ReviseStructuredDraftAsync(
                current.BodyDocumentJson,
                feedback,
                ct);

            var version = await _repo.CreateAssetVersionAsync(
                new CreateContentAssetVersionCommand(current.AssetId, revisedJson),
                ct);
            return Ok(version);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Revise misconfigured for {Provider}", provider);
            return StatusCode(503, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Revise provider call failed");
            return StatusCode(502, "LLM provider request failed");
        }
    }

    /// <summary>
    /// Pillar → multi-channel short-form / ad companion assets (Copy.ai-class pack).
    /// </summary>
    [HttpPost("{id:guid}/repurpose")]
    public async Task<ActionResult<RepurposeGcwResult>> Repurpose(
        Guid id,
        [FromBody] RepurposeGcwAssetVersionRequest? request,
        CancellationToken ct)
    {
        request ??= new RepurposeGcwAssetVersionRequest();

        if (!Enum.TryParse<ContentGeneratorProvider>(
                request.Provider ?? "OpenAi",
                ignoreCase: true,
                out var provider))
        {
            return BadRequest(
                $"Unknown provider '{request.Provider}'. Valid: {string.Join(", ", Enum.GetNames<ContentGeneratorProvider>())}.");
        }

        if (request.Tone is not null && GcwDraftingCatalog.FindTone(request.Tone) is null)
            return BadRequest($"Unknown tone '{request.Tone}'");

        var current = await _repo.GetAssetVersionByIdAsync(id, ct);
        if (current is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(current.BodyDocumentJson))
            return BadRequest("version has no body document to repurpose");

        var sourceAsset = await _repo.GetAssetByIdAsync(current.AssetId, ct);
        if (sourceAsset is null)
            return NotFound();

        var channelBrief = GcwRepurposeCatalog.BuildChannelBrief(request.Channels);
        var draftingSuffix = GcwDraftingCatalog.BuildPromptSuffix(null, request.Tone);
        if (!string.IsNullOrWhiteSpace(draftingSuffix))
            channelBrief = $"{channelBrief}\n\nTone guidance:\n{draftingSuffix}";

        _logger.LogInformation(
            "GCW user {UserId} repurposing asset version {VersionId} via {Provider} (tone={Tone})",
            _currentUser.UserId,
            id,
            provider,
            request.Tone);

        try
        {
            var generator = _contentGeneratorFactory.Get(provider);
            var packJson = await generator.GenerateRepurposePackAsync(
                current.BodyDocumentJson,
                channelBrief,
                ct);
            var pack = GcwRepurposePack.Parse(packJson);

            var created = new List<RepurposeGcwCreatedItem>();
            var stamp = DateTime.UtcNow.ToString("HHmm");
            foreach (var variant in pack.Variants)
            {
                var name = $"{ChannelLabel(variant.Channel)} · {variant.Title}";
                if (name.Length > 120)
                    name = name[..117] + "…";
                name = $"{name} ({stamp})";

                var asset = await _repo.CreateAssetAsync(
                    new CreateContentAssetCommand(sourceAsset.CampaignId, "companion", name),
                    ct);
                var body = GcwRepurposePack.ToContentDocumentJson(variant);
                var version = await _repo.CreateAssetVersionAsync(
                    new CreateContentAssetVersionCommand(asset.Id, body),
                    ct);

                created.Add(new RepurposeGcwCreatedItem(
                    asset.Id,
                    version.Id,
                    asset.Name,
                    variant.Channel,
                    TruncatePreview(variant.Body, 160)));
            }

            return Ok(new RepurposeGcwResult(
                sourceAsset.Id,
                id,
                sourceAsset.CampaignId,
                created));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Repurpose misconfigured or invalid pack for {Provider}", provider);
            return StatusCode(503, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Repurpose provider call failed");
            return StatusCode(502, "LLM provider request failed");
        }
    }

    /// <summary>
    /// Pillar → YouTube / video SEO companion assets (VidIQ-class pack).
    /// </summary>
    [HttpPost("{id:guid}/video-seo")]
    public async Task<ActionResult<RepurposeGcwResult>> VideoSeo(
        Guid id,
        [FromBody] VideoSeoGcwAssetVersionRequest? request,
        CancellationToken ct)
    {
        request ??= new VideoSeoGcwAssetVersionRequest();

        if (!Enum.TryParse<ContentGeneratorProvider>(
                request.Provider ?? "OpenAi",
                ignoreCase: true,
                out var provider))
        {
            return BadRequest(
                $"Unknown provider '{request.Provider}'. Valid: {string.Join(", ", Enum.GetNames<ContentGeneratorProvider>())}.");
        }

        if (request.Tone is not null && GcwDraftingCatalog.FindTone(request.Tone) is null)
            return BadRequest($"Unknown tone '{request.Tone}'");

        var current = await _repo.GetAssetVersionByIdAsync(id, ct);
        if (current is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(current.BodyDocumentJson))
            return BadRequest("version has no body document for video SEO");

        var sourceAsset = await _repo.GetAssetByIdAsync(current.AssetId, ct);
        if (sourceAsset is null)
            return NotFound();

        var packBrief = GcwVideoSeoPack.BuildPackBrief();
        var draftingSuffix = GcwDraftingCatalog.BuildPromptSuffix(null, request.Tone);
        if (!string.IsNullOrWhiteSpace(draftingSuffix))
            packBrief = $"{packBrief}\n\nTone guidance:\n{draftingSuffix}";

        _logger.LogInformation(
            "GCW user {UserId} generating video SEO pack for asset version {VersionId} via {Provider}",
            _currentUser.UserId,
            id,
            provider);

        try
        {
            var generator = _contentGeneratorFactory.Get(provider);
            var packJson = await generator.GenerateVideoSeoPackAsync(
                current.BodyDocumentJson,
                packBrief,
                ct);
            var pack = GcwVideoSeoPack.Parse(packJson);

            var created = new List<RepurposeGcwCreatedItem>();
            var stamp = DateTime.UtcNow.ToString("HHmm");
            foreach (var section in pack.Sections)
            {
                var label = GcwVideoSeoPack.ChannelLabel(section.Kind);
                var name = $"{label} · {section.Title}";
                if (name.Length > 120)
                    name = name[..117] + "…";
                name = $"{name} ({stamp})";

                var asset = await _repo.CreateAssetAsync(
                    new CreateContentAssetCommand(sourceAsset.CampaignId, "companion", name),
                    ct);
                var body = GcwVideoSeoPack.ToContentDocumentJson(section);
                var version = await _repo.CreateAssetVersionAsync(
                    new CreateContentAssetVersionCommand(asset.Id, body),
                    ct);

                var preview = section.Items.FirstOrDefault()
                              ?? TruncatePreview(section.Body, 160);
                created.Add(new RepurposeGcwCreatedItem(
                    asset.Id,
                    version.Id,
                    asset.Name,
                    section.Kind,
                    preview));
            }

            return Ok(new RepurposeGcwResult(
                sourceAsset.Id,
                id,
                sourceAsset.CampaignId,
                created));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Video SEO misconfigured or invalid pack for {Provider}", provider);
            return StatusCode(503, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Video SEO provider call failed");
            return StatusCode(502, "LLM provider request failed");
        }
    }

    private static string ChannelLabel(string channel) => channel.ToLowerInvariant() switch
    {
        "linkedin" => "LinkedIn",
        "x" => "X",
        "instagram" => "Instagram",
        "meta_ad" => "Meta ad",
        "google_ad" => "Google ad",
        "email" => "Email",
        _ => channel,
    };

    private static string TruncatePreview(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= max)
            return text ?? "";
        return text[..(max - 1)].TrimEnd() + "…";
    }

    public sealed record CreateGcwAssetVersionRequest(Guid AssetId, string BodyDocumentJson);
    public sealed record UpdateGcwAssetVersionRequest(string BodyDocumentJson);
    public sealed record ReviseGcwAssetVersionRequest(
        string Feedback,
        string? Provider = null,
        string? Tone = null);
    public sealed record RepurposeGcwAssetVersionRequest(
        string? Provider = null,
        string? Tone = null,
        string[]? Channels = null);
    public sealed record VideoSeoGcwAssetVersionRequest(
        string? Provider = null,
        string? Tone = null);
    public sealed record RepurposeGcwCreatedItem(
        Guid AssetId,
        Guid VersionId,
        string Name,
        string Channel,
        string Preview);
    public sealed record RepurposeGcwResult(
        Guid SourceAssetId,
        Guid SourceVersionId,
        Guid CampaignId,
        IReadOnlyList<RepurposeGcwCreatedItem> Created);
}

[ApiController]
[Route("api/gcw/review-comments")]
public class GcwReviewCommentsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwReviewCommentsController> _logger;

    public GcwReviewCommentsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwReviewCommentsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReviewCommentDto>>> List(
        [FromQuery] Guid assetVersionId,
        CancellationToken ct)
    {
        if (assetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        var comments = await _repo.GetReviewCommentsByAssetVersionIdAsync(assetVersionId, ct);
        return Ok(comments);
    }

    [HttpPost]
    public async Task<ActionResult<ReviewCommentDto>> Create(
        [FromBody] CreateGcwReviewCommentRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.AssetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("content is required");

        _logger.LogInformation(
            "GCW user {UserId} creating review comment on version {VersionId}",
            _currentUser.UserId,
            request.AssetVersionId);

        var comment = await _repo.CreateReviewCommentAsync(
            new CreateReviewCommentCommand(
                request.AssetVersionId,
                _currentUser.UserId,
                string.IsNullOrWhiteSpace(request.SectionPath) ? null : request.SectionPath.Trim(),
                request.Content.Trim()),
            ct);
        return Ok(comment);
    }

    [HttpPatch("{id:guid}/resolve")]
    public async Task<ActionResult<ReviewCommentDto>> Resolve(
        Guid id,
        [FromBody] ResolveGcwReviewCommentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Resolution))
            return BadRequest("resolution is required");

        _logger.LogInformation(
            "GCW user {UserId} resolving review comment {CommentId}",
            _currentUser.UserId,
            id);

        try
        {
            var comment = await _repo.ResolveReviewCommentAsync(id, request.Resolution.Trim(), ct);
            return Ok(comment);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwReviewCommentRequest(
        Guid AssetVersionId,
        string Content,
        string? SectionPath = null);

    public sealed record ResolveGcwReviewCommentRequest(string Resolution);
}

[ApiController]
[Route("api/gcw/approval-events")]
public class GcwApprovalEventsController : ControllerBase
{
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "submitted",
        "approved",
        "rejected",
        "changes-requested",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwApprovalEventsController> _logger;

    public GcwApprovalEventsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwApprovalEventsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApprovalEventDto>>> List(
        [FromQuery] Guid assetVersionId,
        CancellationToken ct)
    {
        if (assetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        var events = await _repo.GetApprovalEventsByAssetVersionIdAsync(assetVersionId, ct);
        return Ok(events);
    }

    [HttpPost]
    public async Task<ActionResult<ApprovalEventDto>> Create(
        [FromBody] CreateGcwApprovalEventRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.AssetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");
        if (string.IsNullOrWhiteSpace(request.Action))
            return BadRequest("action is required");

        var action = request.Action.Trim();
        if (!AllowedActions.Contains(action))
            return BadRequest($"action must be one of: {string.Join(", ", AllowedActions)}");
        action = AllowedActions.First(a => a.Equals(action, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "GCW user {UserId} creating approval event {Action} on version {VersionId}",
            _currentUser.UserId,
            action,
            request.AssetVersionId);

        var @event = await _repo.CreateApprovalEventAsync(
            new CreateApprovalEventCommand(
                request.AssetVersionId,
                _currentUser.UserId,
                action,
                string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()),
            ct);
        return Ok(@event);
    }

    public sealed record CreateGcwApprovalEventRequest(
        Guid AssetVersionId,
        string Action,
        string? Notes = null);
}
