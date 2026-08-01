using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreator;

[ApiController]
[Route("api/geek-content-creator")]
public class GccController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpGccRepository _repo;
    private readonly GccGenerateService _gen;
    private readonly HttpGeekSeoNicheClient _seo;
    private readonly GccJobStore _jobs;
    private readonly ICurrentUserContext _user;
    private readonly ILogger<GccController> _logger;

    public GccController(
        HttpGccRepository repo,
        GccGenerateService gen,
        HttpGeekSeoNicheClient seo,
        GccJobStore jobs,
        ICurrentUserContext user,
        ILogger<GccController> logger)
    {
        _repo = repo;
        _gen = gen;
        _seo = seo;
        _jobs = jobs;
        _user = user;
        _logger = logger;
    }

    [HttpGet("creates")]
    public async Task<ActionResult<IReadOnlyList<GccCreateDto>>> ListCreates(
        [FromQuery] Guid? clientId,
        CancellationToken ct)
    {
        var list = await _repo.ListCreatesAsync(clientId, _user.UserId.ToString("D"), ct);
        return Ok(list);
    }

    [HttpGet("creates/{id:guid}")]
    public async Task<ActionResult<object>> GetCreate(Guid id, CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();
        var artifacts = await _repo.ListArtifactsAsync(id, ct);
        return Ok(new
        {
            create.Id,
            create.ClientId,
            create.OwnerUserId,
            create.StartingContentType,
            create.Topic,
            create.Notes,
            create.SiteAnalysisId,
            create.SiteSectionJson,
            create.Status,
            create.CreatedAtUtc,
            create.UpdatedAtUtc,
            artifacts,
        });
    }

    [HttpPost("creates")]
    public async Task<ActionResult<GccCreateDto>> CreateCreate(
        [FromBody] CreateCreateRequest request,
        CancellationToken ct)
    {
        if (request is null) return BadRequest("Body required");
        if (request.ClientId == Guid.Empty) return BadRequest("clientId required");
        if (string.IsNullOrWhiteSpace(request.StartingContentType)) return BadRequest("startingContentType required");
        if (string.IsNullOrWhiteSpace(request.Topic)) return BadRequest("topic required");
        if (string.Equals(request.StartingContentType.Trim(), "imagePrompt", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.Notes))
        {
            return BadRequest("Standalone image prompt requires topic and notes");
        }
        if (string.Equals(request.StartingContentType.Trim(), "aiTool", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.Notes))
        {
            return BadRequest("AI Tool create requires a short brief in notes");
        }

        string? sectionJson = null;
        if (request.SiteSection is not null)
        {
            if (request.SiteAnalysisId is Guid aid && aid != Guid.Empty
                && (request.SiteSection.RelatedPages is null || request.SiteSection.RelatedPages.Count == 0))
            {
                return BadRequest("Site Analyzer create requires non-empty relatedPages");
            }
            sectionJson = JsonSerializer.Serialize(request.SiteSection, JsonOpts);
        }

        var created = await _repo.CreateCreateAsync(new CreateGccCreateCommand(
            request.ClientId,
            _user.UserId,
            request.StartingContentType.Trim(),
            request.Topic.Trim(),
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            request.SiteAnalysisId,
            sectionJson), ct);

        return CreatedAtAction(nameof(GetCreate), new { id = created.Id }, created);
    }

    [HttpPost("creates/{id:guid}/generate")]
    public async Task<IActionResult> Generate(Guid id, [FromBody] ProviderRequest? request, CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();

        var section = GccGenerateService.ParseSiteSection(create.SiteSectionJson);
        try
        {
            GccGenerateService.ValidateSiteSectionGate(create.SiteAnalysisId, section);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        if (!TryParseProvider(request?.Provider, out var provider, out var err))
            return BadRequest(err);

        var useAsync = request?.Async == true
            || string.Equals(create.StartingContentType, "pillar", StringComparison.OrdinalIgnoreCase)
            || string.Equals(create.StartingContentType, "aiTool", StringComparison.OrdinalIgnoreCase);

        if (useAsync)
        {
            var job = _jobs.Create("generate", id);
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await RunGenerateAsync(create, section, provider, CancellationToken.None);
                    _jobs.Complete(job.Id, result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Async generate failed for create {CreateId}", id);
                    _jobs.Fail(job.Id, ex.Message);
                }
            });
            return Accepted(new { jobId = job.Id, status = job.Status });
        }

        try
        {
            var result = await RunGenerateAsync(create, section, provider, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Generate validation/config failed");
            return StatusCode(503, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Generate LLM failed");
            return StatusCode(502, "LLM provider request failed");
        }
    }

    [HttpGet("jobs/{id:guid}")]
    public ActionResult<object> GetJob(Guid id)
    {
        var job = _jobs.Get(id);
        if (job is null) return NotFound();
        object? result = null;
        if (!string.IsNullOrWhiteSpace(job.ResultJson))
        {
            try { result = JsonSerializer.Deserialize<object>(job.ResultJson, JsonOpts); }
            catch { result = job.ResultJson; }
        }
        return Ok(new
        {
            job.Id,
            job.Kind,
            job.CreateId,
            job.Status,
            result,
            job.Error,
            job.CreatedAtUtc,
            job.CompletedAtUtc,
        });
    }

    private async Task<object> RunGenerateAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        var id = create.Id;
        if (string.Equals(create.StartingContentType, "aiTool", StringComparison.OrdinalIgnoreCase))
        {
            var names = ParseAiToolNames(create.Topic, create.Notes);
            if (names.Count == 0)
                throw new InvalidOperationException("AI Tool generate requires at least one tool name");

            var created = new List<object>();
            foreach (var name in names)
            {
                var (toolName, body) = await _gen.GenerateToolAsync(
                    name, create.Notes, null, provider, ct);
                var artifact = await _repo.CreateArtifactAsync(
                    new CreateGccArtifactCommand(id, "aiTool", toolName), ct);
                var version = await _repo.CreateVersionAsync(
                    new CreateGccArtifactVersionCommand(artifact.Id, body), ct);
                created.Add(new { artifact, version });
            }
            return new { created };
        }

        var bodyJson = await _gen.GenerateStartingContentAsync(create, section, provider, ct);
        var primaryArtifact = await _repo.CreateArtifactAsync(
            new CreateGccArtifactCommand(id, create.StartingContentType, create.Topic), ct);
        var primaryVersion = await _repo.CreateVersionAsync(
            new CreateGccArtifactVersionCommand(primaryArtifact.Id, bodyJson), ct);
        return new { artifact = primaryArtifact, version = primaryVersion };
    }

    private static List<string> ParseAiToolNames(string topic, string? notes)
    {
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(topic))
            names.Add(topic.Trim());
        if (!string.IsNullOrWhiteSpace(notes))
        {
            var idx = notes.IndexOf("Additional tools:", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var rest = notes[(idx + "Additional tools:".Length)..];
                foreach (var part in rest.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!string.IsNullOrWhiteSpace(part) &&
                        !names.Contains(part, StringComparer.OrdinalIgnoreCase))
                        names.Add(part);
                }
            }
        }
        return names;
    }

    [HttpGet("artifacts")]
    public async Task<ActionResult<IReadOnlyList<GccArtifactDto>>> ListArtifacts([FromQuery] Guid createId, CancellationToken ct)
    {
        if (createId == Guid.Empty) return BadRequest("createId required");
        return Ok(await _repo.ListArtifactsAsync(createId, ct));
    }

    [HttpGet("versions")]
    public async Task<ActionResult<IReadOnlyList<GccArtifactVersionDto>>> ListVersions([FromQuery] Guid artifactId, CancellationToken ct)
    {
        if (artifactId == Guid.Empty) return BadRequest("artifactId required");
        return Ok(await _repo.ListVersionsAsync(artifactId, ct));
    }

    [HttpGet("versions/{id:guid}")]
    public async Task<ActionResult<GccArtifactVersionDto>> GetVersion(Guid id, CancellationToken ct)
    {
        var v = await _repo.GetVersionAsync(id, ct);
        return v is null ? NotFound() : Ok(v);
    }

    [HttpPost("versions/{id:guid}/revise")]
    public async Task<ActionResult<GccArtifactVersionDto>> Revise(Guid id, [FromBody] ReviseRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Feedback))
            return BadRequest("feedback required");
        if (!TryParseProvider(request.Provider, out var provider, out var err))
            return BadRequest(err);

        var current = await _repo.GetVersionAsync(id, ct);
        if (current is null) return NotFound();

        try
        {
            var revised = await _gen.ReviseAsync(
                current.BodyDocumentJson,
                request.Feedback,
                request.Scope ?? "full",
                request.SectionPath,
                provider,
                ct);
            var version = await _repo.CreateVersionAsync(
                new CreateGccArtifactVersionCommand(current.ArtifactId, revised), ct);
            return Ok(version);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Revise failed");
            return StatusCode(502, "LLM provider request failed");
        }
    }

    [HttpGet("versions/{id:guid}/seo")]
    public async Task<ActionResult> Seo(Guid id, [FromQuery] string keyword, CancellationToken ct)
    {
        var version = await _repo.GetVersionAsync(id, ct);
        if (version is null) return NotFound();
        var report = GccGenerateService.AnalyzeSeo(version.BodyDocumentJson, keyword ?? "");
        return Ok(report);
    }

    [HttpGet("versions/{id:guid}/polish")]
    public async Task<ActionResult> Polish(Guid id, CancellationToken ct)
    {
        var version = await _repo.GetVersionAsync(id, ct);
        if (version is null) return NotFound();
        var report = GccGenerateService.AnalyzePolish(version.BodyDocumentJson);
        return Ok(report);
    }

    [HttpPost("versions/{id:guid}/approve")]
    public async Task<ActionResult<object>> Approve(Guid id, [FromBody] ApproveRequest? request, CancellationToken ct)
    {
        var version = await _repo.GetVersionAsync(id, ct);
        if (version is null) return NotFound();
        var artifact = await _repo.UpdateArtifactStatusAsync(version.ArtifactId, "approved", ct);
        var evt = await _repo.CreateApprovalEventAsync(new CreateGccApprovalEventCommand(
            version.Id,
            _user.UserId,
            "approved",
            request?.Notes), ct);
        return Ok(new { artifact, @event = evt });
    }

    [HttpPost("versions/{id:guid}/repurpose")]
    public async Task<ActionResult<object>> Repurpose(Guid id, [FromBody] MixRequest? request, CancellationToken ct)
    {
        request ??= new MixRequest(false, false, 0, 0, 0, 0, 0, 0, null, null, false, null);
        var version = await _repo.GetVersionAsync(id, ct);
        if (version is null) return NotFound();
        var artifact = await _repo.GetArtifactAsync(version.ArtifactId, ct);
        if (artifact is null) return NotFound();
        if (!string.Equals(artifact.Status, "approved", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Content approval required before Repurpose");

        if (!TryParseProvider(request?.Provider, out var provider, out var err))
            return BadRequest(err);

        var createId = artifact.CreateId;
        var created = new List<object>();

        var packChannels = new List<string>();
        if ((request?.LinkedInCount ?? 0) > 0) packChannels.Add("LinkedIn");
        if ((request?.XCount ?? 0) > 0) packChannels.Add("X");
        if ((request?.InstagramCount ?? 0) > 0) packChannels.Add("Instagram");
        if ((request?.MetaAdsCount ?? 0) > 0) packChannels.Add("MetaAds");
        if ((request?.GoogleAdsCount ?? 0) > 0) packChannels.Add("GoogleAds");

        try
        {
            if (packChannels.Count > 0)
            {
                var packJson = await _gen.GenerateRepurposePackAsync(version.BodyDocumentJson, packChannels, provider, ct);
                var packArtifact = await _repo.CreateArtifactAsync(
                    new CreateGccArtifactCommand(createId, "socialPack", "Repurpose pack"), ct);
                var packVersion = await _repo.CreateVersionAsync(
                    new CreateGccArtifactVersionCommand(packArtifact.Id, packJson), ct);
                created.Add(new { artifact = packArtifact, version = packVersion });
            }

            var emailCount = request?.EmailCount ?? 0;
            for (var i = 0; i < emailCount; i++)
            {
                var emailBody = await _gen.GenerateRepurposePackAsync(
                    version.BodyDocumentJson,
                    new[] { "Email" },
                    provider,
                    ct);
                var emailArtifact = await _repo.CreateArtifactAsync(
                    new CreateGccArtifactCommand(createId, "email", $"Email {i + 1}"), ct);
                var emailVersion = await _repo.CreateVersionAsync(
                    new CreateGccArtifactVersionCommand(emailArtifact.Id, emailBody), ct);
                created.Add(new { artifact = emailArtifact, version = emailVersion });
            }

            if (request?.Blog == true)
            {
                var blogJson = await _gen.GenerateStartingContentAsync(
                    new GccCreateDto(Guid.Empty, Guid.Empty, Guid.Empty, "blog", artifact.Name, "Repurpose blog from approved artifact", null, null, "draft", DateTime.UtcNow, DateTime.UtcNow),
                    null,
                    provider,
                    ct);
                // Use revise-style from source for better grounding
                blogJson = await _gen.ReviseAsync(version.BodyDocumentJson, "Rewrite as a standalone blog post.", "full", null, provider, ct);
                var blogArtifact = await _repo.CreateArtifactAsync(
                    new CreateGccArtifactCommand(createId, "blog", $"{artifact.Name} — Blog"), ct);
                var blogVersion = await _repo.CreateVersionAsync(
                    new CreateGccArtifactVersionCommand(blogArtifact.Id, blogJson), ct);
                created.Add(new { artifact = blogArtifact, version = blogVersion });
            }

            if (request?.TechArticle == true)
            {
                var techJson = await _gen.ReviseAsync(
                    version.BodyDocumentJson,
                    "Rewrite as a TechArticle with deeper technical sections.",
                    "full",
                    null,
                    provider,
                    ct);
                var techArtifact = await _repo.CreateArtifactAsync(
                    new CreateGccArtifactCommand(createId, "techArticle", $"{artifact.Name} — TechArticle"), ct);
                var techVersion = await _repo.CreateVersionAsync(
                    new CreateGccArtifactVersionCommand(techArtifact.Id, techJson), ct);
                created.Add(new { artifact = techArtifact, version = techVersion });
            }

            if (request?.ImagePrompts == true)
            {
                var promptJson = await _gen.GenerateImagePromptJsonAsync(
                    artifact.Name,
                    null,
                    version.BodyDocumentJson,
                    provider,
                    ct);
                var promptArtifact = await _repo.CreateArtifactAsync(
                    new CreateGccArtifactCommand(createId, "imagePrompt", $"{artifact.Name} — Image prompt"), ct);
                var promptVersion = await _repo.CreateVersionAsync(
                    new CreateGccArtifactVersionCommand(promptArtifact.Id, promptJson), ct);
                created.Add(new { artifact = promptArtifact, version = promptVersion });
            }

            var toolNames = request?.AiToolNames?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct().ToList()
                ?? new List<string>();
            if (toolNames.Count > 0)
            {
                foreach (var name in toolNames)
                {
                    var (toolName, body) = await _gen.GenerateToolAsync(
                        name,
                        request?.AiToolBrief,
                        version.BodyDocumentJson,
                        provider,
                        ct);
                    var toolArtifact = await _repo.CreateArtifactAsync(
                        new CreateGccArtifactCommand(createId, "aiTool", toolName), ct);
                    var toolVersion = await _repo.CreateVersionAsync(
                        new CreateGccArtifactVersionCommand(toolArtifact.Id, body), ct);
                    created.Add(new { artifact = toolArtifact, version = toolVersion });
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Repurpose failed");
            return StatusCode(502, "LLM provider request failed");
        }

        return Ok(new { created });
    }

    [HttpPost("tools/generate")]
    public async Task<ActionResult<object>> GenerateTools([FromBody] ToolGenerateRequest request, CancellationToken ct)
    {
        if (request is null || request.CreateId == Guid.Empty)
            return BadRequest("createId required");

        var names = (request.SelectedNames?.Count > 0 ? request.SelectedNames : request.ToolNames)
            ?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct().ToList()
            ?? new List<string>();
        if (names.Count == 0)
            return BadRequest("toolNames required (non-empty after trim)");

        if (request.SourceArtifactId is null && string.IsNullOrWhiteSpace(request.Brief))
            return BadRequest("brief required when no sourceArtifactId");

        if (!TryParseProvider(request.Provider, out var provider, out var err))
            return BadRequest(err);

        string? sourceContext = null;
        if (request.SourceArtifactId is Guid sid)
        {
            var versions = await _repo.ListVersionsAsync(sid, ct);
            sourceContext = versions.FirstOrDefault()?.BodyDocumentJson;
        }

        var created = new List<object>();
        try
        {
            foreach (var name in names)
            {
                var (toolName, body) = await _gen.GenerateToolAsync(name, request.Brief, sourceContext, provider, ct);
                var artifact = await _repo.CreateArtifactAsync(
                    new CreateGccArtifactCommand(request.CreateId, "aiTool", toolName), ct);
                var version = await _repo.CreateVersionAsync(
                    new CreateGccArtifactVersionCommand(artifact.Id, body), ct);
                created.Add(new { artifact, version });
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Tool generate failed");
            return StatusCode(502, "LLM provider request failed");
        }

        return Ok(new { created });
    }

    [HttpPost("image-prompts/generate")]
    public async Task<ActionResult<object>> GenerateImagePrompt([FromBody] ImagePromptRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest("Body required");
        if (!TryParseProvider(request.Provider, out var provider, out var err))
            return BadRequest(err);

        string? artifactContext = null;
        if (request.SourceArtifactId is Guid sid)
        {
            var versions = await _repo.ListVersionsAsync(sid, ct);
            artifactContext = versions.FirstOrDefault()?.BodyDocumentJson;
        }

        if (string.IsNullOrWhiteSpace(artifactContext)
            && (string.IsNullOrWhiteSpace(request.Topic) || string.IsNullOrWhiteSpace(request.Notes)))
        {
            return BadRequest("Standalone image prompt requires topic and notes");
        }

        if (request.CreateId is null || request.CreateId == Guid.Empty)
            return BadRequest("createId required");

        try
        {
            var json = await _gen.GenerateImagePromptJsonAsync(
                request.Topic ?? "Image",
                request.Notes,
                artifactContext,
                provider,
                ct);
            var artifact = await _repo.CreateArtifactAsync(
                new CreateGccArtifactCommand(request.CreateId.Value, "imagePrompt", request.Topic ?? "Image prompt"), ct);
            var version = await _repo.CreateVersionAsync(
                new CreateGccArtifactVersionCommand(artifact.Id, json), ct);
            return Ok(new { artifact, version });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Image prompt generate failed");
            return StatusCode(502, "LLM provider request failed");
        }
    }

    [HttpPost("site-analyzer/analyze")]
    public async Task<ActionResult<SiteAnalysisDto>> AnalyzeSite(
        [FromBody] AnalyzeSiteRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Domain))
            return BadRequest("domain required");

        if (request.NicheProfileId is Guid profileId && profileId != Guid.Empty && _seo.IsEnabled)
        {
            var auth = Request.Headers.Authorization.ToString();
            var bearer = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? auth["Bearer ".Length..].Trim()
                : null;
            var live = await _seo.GetGapsAsync(profileId, bearer, ct);
            if (live is { Count: > 0 })
            {
                var gaps = live.Select(g => new ContentGapDto(
                    g.Id, g.Topic, g.SectionPath, g.Reason, g.SuggestPillar)).ToList();
                var persisted = await _repo.CreateSiteAnalysisAsync(
                    new CreateGccSiteAnalysisCommand(
                        Id: null,
                        Domain: request.Domain.Trim(),
                        SeedTopic: request.SeedTopic,
                        GapsJson: GccGenerateService.SerializeGaps(gaps),
                        IsDemo: false),
                    ct);
                return Ok(new
                {
                    id = persisted.Id,
                    domain = persisted.Domain,
                    status = "ready",
                    isDemo = false,
                    gaps,
                });
            }
        }

        var demoGaps = GccGenerateService.BuildDemoGaps(request.SeedTopic);
        var demo = await _repo.CreateSiteAnalysisAsync(
            new CreateGccSiteAnalysisCommand(
                Id: null,
                Domain: request.Domain.Trim(),
                SeedTopic: request.SeedTopic,
                GapsJson: GccGenerateService.SerializeGaps(demoGaps),
                IsDemo: true),
            ct);
        return Ok(new
        {
            id = demo.Id,
            domain = demo.Domain,
            status = "ready",
            isDemo = true,
            gaps = demoGaps,
        });
    }

    [HttpGet("site-analyzer/{id:guid}/gaps")]
    public async Task<ActionResult<IReadOnlyList<ContentGapDto>>> Gaps(Guid id, CancellationToken ct)
    {
        var analysis = await _repo.GetSiteAnalysisAsync(id, ct);
        if (analysis is null) return NotFound();
        return Ok(GccGenerateService.DeserializeGaps(analysis.GapsJson));
    }

    [HttpGet("site-analyzer/{id:guid}/section-context")]
    public async Task<ActionResult<SiteSectionContextDto>> SectionContext(
        Guid id,
        [FromQuery] string gapTopic,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gapTopic)) return BadRequest("gapTopic required");
        var analysis = await _repo.GetSiteAnalysisAsync(id, ct);
        if (analysis is null) return NotFound();
        var gaps = GccGenerateService.DeserializeGaps(analysis.GapsJson);
        return Ok(GccGenerateService.BuildSectionContext(
            analysis.Id,
            analysis.Domain,
            analysis.SeedTopic,
            gaps,
            gapTopic));
    }

    private static bool TryParseProvider(string? raw, out ContentGeneratorProvider provider, out string? error)
    {
        provider = ContentGeneratorProvider.OpenAi;
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (Enum.TryParse(raw, ignoreCase: true, out provider)) return true;
        error = $"Unknown provider '{raw}'. Valid: {string.Join(", ", Enum.GetNames<ContentGeneratorProvider>())}.";
        return false;
    }

    public sealed record CreateCreateRequest(
        Guid ClientId,
        string StartingContentType,
        string Topic,
        string? Notes,
        Guid? SiteAnalysisId,
        SiteSectionContextDto? SiteSection);

    public sealed record ProviderRequest(string? Provider, bool Async = false);
    public sealed record ReviseRequest(string Feedback, string? Scope, string? SectionPath, string? Provider);
    public sealed record ApproveRequest(string? Notes);
    public sealed record MixRequest(
        bool Blog,
        bool TechArticle,
        int EmailCount,
        int LinkedInCount,
        int XCount,
        int InstagramCount,
        int MetaAdsCount,
        int GoogleAdsCount,
        IReadOnlyList<string>? AiToolNames,
        string? AiToolBrief,
        bool ImagePrompts,
        string? Provider);
    public sealed record ToolGenerateRequest(
        IReadOnlyList<string>? ToolNames,
        string? Brief,
        Guid? SourceArtifactId,
        IReadOnlyList<string>? SelectedNames,
        Guid CreateId,
        string? Provider);
    public sealed record ImagePromptRequest(
        Guid? CreateId,
        string? Topic,
        string? Notes,
        Guid? SourceArtifactId,
        string? Provider);
    public sealed record AnalyzeSiteRequest(string Domain, string? SeedTopic, Guid? NicheProfileId = null);
}
