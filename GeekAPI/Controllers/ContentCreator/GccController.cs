using System.Text.Json;
using ContentWriter.Application.DTOs;
using ContentWriter.Application.Services;
using ContentWriter.Domain.Entities;
using ContentWriter.Domain.Enums;
using ContentWriter.Infrastructure;
using ContentWriter.Infrastructure.InMemory;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    private readonly IProjectStore _projects;
    private readonly IContentGenerationOrchestrator _orchestrator;
    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccController> _logger;

    public GccController(
        HttpGccRepository repo,
        GccGenerateService gen,
        HttpGeekSeoNicheClient seo,
        GccJobStore jobs,
        ICurrentUserContext user,
        IProjectStore projects,
        IContentGenerationOrchestrator orchestrator,
        IOptions<CompanyProfileOptions> company,
        ILogger<GccController> logger)
    {
        _repo = repo;
        _gen = gen;
        _seo = seo;
        _jobs = jobs;
        _user = user;
        _projects = projects;
        _orchestrator = orchestrator;
        _company = company.Value;
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

        try
        {
            var result = await RunGenerateAsync(_repo, _gen, create, section, provider, ct);
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
        // Kept for older clients; generate is synchronous — no in-process job runner.
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
        HttpGccRepository repo,
        GccGenerateService gen,
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
                var (toolName, document, _, _) = await gen.GenerateToolAsync(
                    name, create.Notes, null, provider, ct);
                var body = GccGenerateService.SerializeDocument(document);
                var artifact = await repo.CreateArtifactAsync(
                    new CreateGccArtifactCommand(id, "aiTool", toolName), ct);
                var version = await repo.CreateVersionAsync(
                    new CreateGccArtifactVersionCommand(artifact.Id, body), ct);
                created.Add(new { artifact, version });
            }
            return new { created };
        }

        var bodyJson = await gen.GenerateStartingContentAsync(create, section, provider, ct);
        var primaryArtifact = await repo.CreateArtifactAsync(
            new CreateGccArtifactCommand(id, create.StartingContentType, create.Topic), ct);
        var primaryVersion = await repo.CreateVersionAsync(
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
                var blogJson = await _gen.ReviseAsync(
                    version.BodyDocumentJson,
                    "Rewrite as a standalone blog post.",
                    "full",
                    null,
                    provider,
                    ct);
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
                    var (toolName, document, _, _) = await _gen.GenerateToolAsync(
                        name,
                        request?.AiToolBrief,
                        version.BodyDocumentJson,
                        provider,
                        ct);
                    var toolArtifact = await _repo.CreateArtifactAsync(
                        new CreateGccArtifactCommand(createId, "aiTool", toolName), ct);
                    var toolVersion = await _repo.CreateVersionAsync(
                        new CreateGccArtifactVersionCommand(
                            toolArtifact.Id,
                            GccGenerateService.SerializeDocument(document)), ct);
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
                var (toolName, document, _, _) = await _gen.GenerateToolAsync(name, request.Brief, sourceContext, provider, ct);
                var artifact = await _repo.CreateArtifactAsync(
                    new CreateGccArtifactCommand(request.CreateId, "aiTool", toolName), ct);
                var version = await _repo.CreateVersionAsync(
                    new CreateGccArtifactVersionCommand(
                        artifact.Id,
                        GccGenerateService.SerializeDocument(document)), ct);
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

    /// <summary>
    /// Content Creator addition on CWV2 projects: generate tool pages from human-supplied names + brief
    /// using CWV2 tool prompts — does <b>not</b> require a pillar Tools section.
    /// </summary>
    [HttpPost("projects/{projectId:guid}/tools-from-names")]
    public async Task<IActionResult> GenerateToolsFromNames(
        Guid projectId,
        [FromBody] ToolsFromNamesRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body required");

        var names = (request.ToolNames ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (names.Count == 0)
            return BadRequest("toolNames required");
        if (string.IsNullOrWhiteSpace(request.Brief))
            return BadRequest("brief required");

        if (!TryParseProvider(request.Provider, out var provider, out var err))
            return BadRequest(err);

        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return NotFound();

        var usedSlugs = new HashSet<string>(
            project.GeneratedContents
                .Where(c => c.ContentType == GeneratedContentType.ToolPost)
                .Select(c => c.Slug),
            StringComparer.OrdinalIgnoreCase);

        var order = project.GeneratedContents.Count(c => c.ContentType == GeneratedContentType.ToolPost);
        try
        {
            foreach (var name in names)
            {
                var relatedPillar = project.GeneratedContents.FirstOrDefault(c =>
                    c.ContentType == GeneratedContentType.TechnicalArticle
                    && !string.IsNullOrWhiteSpace(c.Slug));
                var relatedArticleUrl = relatedPillar is null
                    ? null
                    : $"{_company.ArticleBaseUrl.TrimEnd('/')}/{project.Department}/{relatedPillar.Slug}";

                var slug = SlugHelper.EnsureUniqueSlug(SlugHelper.Slugify(name), usedSlugs);
                order += 1;

                var tool = await _gen.GenerateToolPageAsync(
                    name,
                    request.Brief,
                    sourceContext: null,
                    department: project.Department,
                    relatedArticleUrl: relatedArticleUrl,
                    provider: provider,
                    ct: ct,
                    preferredSlug: slug);

                var meta = tool.Metadata;

                // Replace existing tool with same slug if regenerating.
                var existing = project.GeneratedContents
                    .FirstOrDefault(c =>
                        c.ContentType == GeneratedContentType.ToolPost
                        && string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                    project.GeneratedContents.Remove(existing);

                project.GeneratedContents.Add(new GeneratedContent
                {
                    ProjectId = project.Id,
                    ContentType = GeneratedContentType.ToolPost,
                    Title = tool.Name,
                    DisplayTitle = tool.Name,
                    Slug = slug,
                    Summary = meta.Summary,
                    MainSummary = meta.MainSummary,
                    HeroSummary = meta.HeroSummary,
                    HomeSummary = meta.HomeSummary,
                    BlogSummary = meta.BlogSummary,
                    DepartmentListExcerpt = meta.DepartmentListExcerpt,
                    ToolPageExcerpt = meta.ToolPageExcerpt,
                    AdvertisingSummary = meta.AdvertisingSummary,
                    MetaDescription = meta.MetaDescription,
                    Body = tool.Document,
                    LedeType = LedeType.Summary,
                    JsonLdSchema = tool.JsonLdSchema,
                    RelatedArticleUrl = tool.RelatedArticleUrl,
                    SourceAppName = tool.Name,
                    SourceAppOrder = order,
                    WordCount = tool.WordCount,
                    GeneratedByProvider = provider == ContentGeneratorProvider.Anthropic
                        ? LlmProviderType.Anthropic
                        : LlmProviderType.OpenAi,
                    GeneratedByModel = provider.ToString(),
                });
            }

            project.UpdatedAtUtc = DateTime.UtcNow;
            await _projects.SaveAsync(project, ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Tools-from-names validation failed for {ProjectId}", projectId);
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Tools-from-names LLM failed for {ProjectId}", projectId);
            return StatusCode(502, "LLM provider request failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tools-from-names failed for {ProjectId}", projectId);
            return StatusCode(502, ex.Message);
        }

        var set = GeneratedContentSetAssembler.Assemble(
            project,
            project.Department,
            _company.ArticleBaseUrl,
            _company.BlogBaseUrl,
            _company.ToolBaseUrl);
        return Ok(set);
    }

    /// <summary>
    /// Plan §7 social/ads pack: one LLM call for chosen channel slots (counts), not one call per post.
    /// Maps Facebook/LinkedIn variants onto CWV2 social rows; full pack JSON returned for other channels.
    /// </summary>
    [HttpPost("projects/{projectId:guid}/social-pack")]
    public async Task<IActionResult> GenerateSocialPack(
        Guid projectId,
        [FromBody] SocialPackRequest? request,
        CancellationToken ct)
    {
        request ??= new SocialPackRequest(1, 1, 0, 0, 0, 0, null);
        if (!TryParseProvider(request.Provider, out var provider, out var err))
            return BadRequest(err);

        var channels = BuildPackChannels(request);
        if (channels.Count == 0)
            return BadRequest("Select at least one social/ads channel count.");

        var project = await _projects.GetAsync(projectId, ct);
        if (project is null) return NotFound();

        GeneratedContent? pillar = project.GeneratedContents.FirstOrDefault(c =>
            c.ContentType == GeneratedContentType.TechnicalArticle
            && c.Body is not null
            && c.WordCount >= 200);
        GeneratedContent? blog = project.GeneratedContents.FirstOrDefault(c =>
            c.ContentType == GeneratedContentType.BlogPost
            && c.Body is not null
            && c.WordCount >= 100);
        var sourceRow = pillar ?? blog;
        if (sourceRow?.Body is null)
            return BadRequest("Generate a pillar body or a blog before social/ads pack.");

        var sourceJson = JsonSerializer.Serialize(new
        {
            title = sourceRow.DisplayTitle ?? sourceRow.Title,
            body = ContentDocumentText.Flatten(sourceRow.Body),
        }, JsonOpts);

        var slugBase = sourceRow.Slug;
        var sourceUrl = pillar is not null
            ? $"{_company.ArticleBaseUrl.TrimEnd('/')}/{project.Department}/{slugBase}"
            : $"{_company.BlogBaseUrl.TrimEnd('/')}/{project.Department}/{slugBase}";

        try
        {
            var packJson = await _gen.GenerateRepurposePackAsync(sourceJson, channels, provider, ct);
            var variants = GccGenerateService.ParsePackVariants(packJson);

            // Replace prior CWV2 social rows (pack is authoritative for this run).
            foreach (var existing in project.GeneratedContents
                         .Where(c => c.ContentType is GeneratedContentType.SocialFacebook
                             or GeneratedContentType.SocialLinkedIn)
                         .ToList())
            {
                project.GeneratedContents.Remove(existing);
            }

            var fb = variants.FirstOrDefault(v =>
                v.Channel.Contains("facebook", StringComparison.OrdinalIgnoreCase)
                || v.Channel.Equals("Meta", StringComparison.OrdinalIgnoreCase));
            var li = variants.FirstOrDefault(v =>
                v.Channel.Contains("linkedin", StringComparison.OrdinalIgnoreCase));

            if (fb is not null)
            {
                project.GeneratedContents.Add(new GeneratedContent
                {
                    ProjectId = project.Id,
                    ContentType = GeneratedContentType.SocialFacebook,
                    Title = string.IsNullOrWhiteSpace(fb.Title)
                        ? $"{sourceRow.Title} (Facebook)"
                        : fb.Title,
                    Slug = $"{slugBase}-facebook",
                    Body = ContentDocumentText.FromPlainText(FormatPackBody(fb)),
                    RelatedArticleUrl = sourceUrl,
                    MetaDescription = TruncateMeta(fb.Cta),
                    GeneratedByProvider = ToLlm(provider),
                    GeneratedByModel = provider.ToString(),
                });
            }

            if (li is not null)
            {
                project.GeneratedContents.Add(new GeneratedContent
                {
                    ProjectId = project.Id,
                    ContentType = GeneratedContentType.SocialLinkedIn,
                    Title = string.IsNullOrWhiteSpace(li.Title)
                        ? $"{sourceRow.Title} (LinkedIn)"
                        : li.Title,
                    Slug = $"{slugBase}-linkedin",
                    Body = ContentDocumentText.FromPlainText(FormatPackBody(li)),
                    RelatedArticleUrl = sourceUrl,
                    // Full pack retained for Mix channels beyond FB/LI (inspectable, not invented later).
                    MetaDescription = TruncateMeta(packJson),
                    GeneratedByProvider = ToLlm(provider),
                    GeneratedByModel = provider.ToString(),
                });
            }
            else
            {
                // Pack had no LinkedIn slot — still persist pack JSON on a LinkedIn-typed row for retrieval.
                var summary = string.Join(
                    "\n\n---\n\n",
                    variants.Select(v => $"[{v.Channel}]\n{FormatPackBody(v)}"));
                project.GeneratedContents.Add(new GeneratedContent
                {
                    ProjectId = project.Id,
                    ContentType = GeneratedContentType.SocialLinkedIn,
                    Title = $"{sourceRow.Title} (Social pack)",
                    Slug = $"{slugBase}-social-pack",
                    Body = ContentDocumentText.FromPlainText(summary),
                    RelatedArticleUrl = sourceUrl,
                    MetaDescription = TruncateMeta(packJson),
                    GeneratedByProvider = ToLlm(provider),
                    GeneratedByModel = provider.ToString(),
                });
            }

            project.UpdatedAtUtc = DateTime.UtcNow;
            await _projects.SaveAsync(project, ct);

            var set = GeneratedContentSetAssembler.Assemble(
                project,
                project.Department,
                _company.ArticleBaseUrl,
                _company.BlogBaseUrl,
                _company.ToolBaseUrl);
            return Ok(new
            {
                set,
                packJson,
                channels,
                variantCount = variants.Count,
                llmCalls = 1,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Social pack LLM failed for {ProjectId}", projectId);
            return StatusCode(502, "LLM provider request failed");
        }
    }

    private static List<string> BuildPackChannels(SocialPackRequest request)
    {
        var channels = new List<string>();
        void Add(string name, int count)
        {
            for (var i = 0; i < Math.Max(0, count); i++)
                channels.Add(name);
        }

        Add("Facebook", request.FacebookCount);
        Add("LinkedIn", request.LinkedInCount);
        Add("X", request.XCount);
        Add("Instagram", request.InstagramCount);
        Add("MetaAds", request.MetaAdsCount);
        Add("GoogleAds", request.GoogleAdsCount);
        return channels;
    }

    private static string FormatPackBody(GccGenerateService.PackVariant v)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(v.Headline))
            sb.AppendLine(v.Headline);
        sb.AppendLine(v.Body);
        if (!string.IsNullOrWhiteSpace(v.Cta))
            sb.AppendLine(v.Cta);
        return sb.ToString().Trim();
    }

    private static string? TruncateMeta(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= 4000 ? value : value[..4000];
    }

    private static LlmProviderType ToLlm(ContentGeneratorProvider provider) =>
        provider == ContentGeneratorProvider.Anthropic
            ? LlmProviderType.Anthropic
            : LlmProviderType.OpenAi;

    /// <summary>
    /// Names operators can pick for AI Tools — from pillar Tools section and existing tool drafts.
    /// </summary>
    [HttpGet("projects/{projectId:guid}/tool-name-candidates")]
    public async Task<IActionResult> ToolNameCandidates(Guid projectId, CancellationToken ct)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return NotFound();

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var trimmed = name.Trim();
            if (seen.Add(trimmed)) names.Add(trimmed);
        }

        var pillar = project.GeneratedContents.FirstOrDefault(c =>
            c.ContentType == GeneratedContentType.TechnicalArticle
            && c.Body is not null
            && c.WordCount >= 200);
        if (pillar is not null)
        {
            foreach (var app in ToolSectionExtractor.ExtractApplications(pillar.Body, pillar.SectionOutline))
                Add(app.Name);
        }

        foreach (var tool in project.GeneratedContents
                     .Where(c => c.ContentType == GeneratedContentType.ToolPost)
                     .OrderBy(c => c.SourceAppOrder ?? int.MaxValue))
        {
            Add(string.IsNullOrWhiteSpace(tool.DisplayTitle) ? tool.Title : tool.DisplayTitle);
            Add(tool.SourceAppName);
        }

        // Desired headings often list tool names before a pillar exists.
        if (!string.IsNullOrWhiteSpace(project.Notes))
        {
            foreach (var part in project.Notes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Add(part);
        }

        return Ok(new { names = names.Take(12).ToList() });
    }

    /// <summary>Image-prompt rows on a CWV2 project (for Revise picker).</summary>
    [HttpGet("projects/{projectId:guid}/image-prompt-rows")]
    public async Task<IActionResult> ImagePromptRows(Guid projectId, CancellationToken ct)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return NotFound();

        var rows = project.GeneratedContents
            .Where(c => c.ContentType is GeneratedContentType.ImagePromptSection
                or GeneratedContentType.ImagePromptPillarFigure
                or GeneratedContentType.ImagePromptBlogFigure)
            .OrderBy(c => c.Title)
            .Select(c => new
            {
                slug = c.Slug,
                title = c.Title,
                contentType = c.ContentType.ToString(),
                promptPreview = ContentDocumentText.Flatten(c.Body),
            })
            .ToList();

        return Ok(new { rows });
    }

    /// <summary>
    /// Content Creator addition: operator Revise (Full/Section) on a CWV2 project draft.
    /// New body replaces the selected GeneratedContent row (not a multi-turn chat).
    /// </summary>
    [HttpPost("projects/{projectId:guid}/revise")]
    public async Task<IActionResult> ReviseProjectContent(
        Guid projectId,
        [FromBody] ProjectReviseRequest? request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Feedback))
            return BadRequest("feedback required");
        if (string.IsNullOrWhiteSpace(request.ContentType) && string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest("contentType or slug required");
        if (!TryParseProvider(request.Provider, out var provider, out var err))
            return BadRequest(err);

        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return NotFound();

        GeneratedContent? row = null;
        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            row = project.GeneratedContents.FirstOrDefault(c =>
                string.Equals(c.Slug, request.Slug, StringComparison.OrdinalIgnoreCase));
        }
        else if (Enum.TryParse<GeneratedContentType>(request.ContentType, ignoreCase: true, out var contentType))
        {
            row = contentType switch
            {
                GeneratedContentType.ToolPost when !string.IsNullOrWhiteSpace(request.ToolSlug) =>
                    project.GeneratedContents.FirstOrDefault(c =>
                        c.ContentType == GeneratedContentType.ToolPost
                        && string.Equals(c.Slug, request.ToolSlug, StringComparison.OrdinalIgnoreCase)),
                GeneratedContentType.ToolPost =>
                    project.GeneratedContents.FirstOrDefault(c => c.ContentType == GeneratedContentType.ToolPost),
                _ => project.GeneratedContents.FirstOrDefault(c => c.ContentType == contentType),
            };
        }
        else
        {
            return BadRequest($"Unknown contentType '{request.ContentType}'.");
        }

        if (row is null)
            return NotFound("No matching draft on this project.");

        // Image-prompt rows store prompt JSON, not a body document.
        var isImagePrompt = row.ContentType is GeneratedContentType.ImagePromptSection
            or GeneratedContentType.ImagePromptPillarFigure
            or GeneratedContentType.ImagePromptBlogFigure;

        if (!isImagePrompt && row.Body is null)
            return BadRequest("Selected draft has no body document to revise.");

        try
        {
            var notes = request.Feedback.Trim();
            if (string.Equals(request.Scope, "section", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.SectionPath))
                    return BadRequest("sectionPath is required when scope is section.");
                notes =
                    $"Revise ONLY the section at path “{request.SectionPath}”. Leave all other sections unchanged.\n\n{notes}";
            }

            // CWV2 orchestrator with revisionNotes — same path as review rewrite. Not CWV3.
            _ = provider; // provider selection remains on the project PreferredProvider / CWV2 stack
            GeneratedContentSet set;
            if (isImagePrompt)
            {
                set = await _orchestrator.GenerateImagePromptsAsync(
                    projectId,
                    sectionHeadingsToTest: string.IsNullOrWhiteSpace(row.Title)
                        ? null
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.Title },
                    cancellationToken: ct);
            }
            else
            {
                set = row.ContentType switch
                {
                    GeneratedContentType.TechnicalArticle =>
                        await _orchestrator.GeneratePillarBodyAsync(projectId, notes, ct),
                    GeneratedContentType.BlogPost =>
                        await _orchestrator.GenerateBlogAsync(projectId, notes, ct),
                    GeneratedContentType.ToolPost =>
                        await _orchestrator.GenerateToolPagesAsync(
                            projectId,
                            notes,
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.Slug },
                            ct),
                    _ => throw new InvalidOperationException(
                        $"Revise is not supported for content type '{row.ContentType}'."),
                };
            }

            return Ok(set);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Project revise LLM failed for {ProjectId}", projectId);
            return StatusCode(502, "LLM provider request failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project revise failed for {ProjectId}", projectId);
            return StatusCode(502, ex.Message);
        }
    }

    // ParseToolBodyJson removed — tools-from-names keeps the CWV2 ContentDocument in memory.

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

        try
        {
            var json = await _gen.GenerateImagePromptJsonAsync(
                request.Topic ?? "Image",
                request.Notes,
                artifactContext,
                provider,
                ct);

            // Standalone path (no Create): return prompt JSON only — CWV2-style, no homemade create workspace.
            if (request.CreateId is null || request.CreateId == Guid.Empty)
            {
                object? parsed = null;
                try { parsed = JsonSerializer.Deserialize<object>(json, JsonOpts); } catch { /* keep raw */ }
                return Ok(new { promptJson = json, prompt = parsed });
            }

            var artifact = await _repo.CreateArtifactAsync(
                new CreateGccArtifactCommand(request.CreateId.Value, "imagePrompt", request.Topic ?? "Image prompt"), ct);
            var version = await _repo.CreateVersionAsync(
                new CreateGccArtifactVersionCommand(artifact.Id, json), ct);
            return Ok(new { artifact, version, promptJson = json });
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

    [HttpPost("projects/{projectId:guid}/content-approval")]
    public async Task<IActionResult> SetContentApproval(
        Guid projectId,
        [FromBody] ContentApprovalRequest? request,
        CancellationToken ct)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null) return NotFound();

        var approve = request?.Approved ?? true;
        project.ContentApprovedAtUtc = approve ? DateTime.UtcNow : null;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await _projects.SaveAsync(project, ct);
        return Ok(new { projectId, contentApprovedAtUtc = project.ContentApprovedAtUtc });
    }

    [HttpGet("projects/{projectId:guid}/content-approval")]
    public async Task<IActionResult> GetContentApproval(Guid projectId, CancellationToken ct)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null) return NotFound();
        return Ok(new
        {
            projectId,
            approved = project.ContentApprovedAtUtc is not null,
            contentApprovedAtUtc = project.ContentApprovedAtUtc,
        });
    }

    [HttpPost("site-analyzer/analyze")]
    public async Task<IActionResult> AnalyzeSite(
        [FromBody] AnalyzeSiteRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Domain))
            return BadRequest(new { error = "domain required" });

        var auth = Request.Headers.Authorization.ToString();
        var bearer = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? auth["Bearer ".Length..].Trim()
            : null;

        var loaded = await _seo.LoadSiteModelByDomainAsync(request.Domain, bearer, ct);
        if (!loaded.Ok || loaded.Value is null)
        {
            return StatusCode(
                loaded.StatusCode is >= 400 and < 600 ? loaded.StatusCode : 502,
                new { error = loaded.Error ?? "Failed to load site analysis from Geek-SEO" });
        }

        var snap = loaded.Value;
        var gaps = snap.Gaps.Select(g => new ContentGapDto(
            g.Id, g.Topic, g.SectionPath, g.Reason, g.SuggestPillar)).ToList();

        var payload = new SiteAnalysisStoredPayload(
            gaps,
            snap.SitePages.ToList(),
            snap.TopicalNeighbors.ToList(),
            snap.SeoProfileId,
            snap.SeoProjectId);

        var persisted = await _repo.CreateSiteAnalysisAsync(
            new CreateGccSiteAnalysisCommand(
                Id: null,
                Domain: snap.Domain,
                SeedTopic: request.SeedTopic,
                GapsJson: GccGenerateService.SerializeAnalysisPayload(payload),
                IsDemo: false),
            ct);

        return Ok(new
        {
            id = persisted.Id,
            domain = persisted.Domain,
            status = "ready",
            seoProjectId = snap.SeoProjectId,
            seoProfileId = snap.SeoProfileId,
            gaps,
        });
    }

    [HttpGet("site-analyzer/{id:guid}/gaps")]
    public async Task<IActionResult> Gaps(Guid id, CancellationToken ct)
    {
        var analysis = await _repo.GetSiteAnalysisAsync(id, ct);
        if (analysis is null) return NotFound();
        try
        {
            return Ok(GccGenerateService.DeserializeGaps(analysis.GapsJson));
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    [HttpGet("site-analyzer/{id:guid}/section-context")]
    public async Task<IActionResult> SectionContext(
        Guid id,
        [FromQuery] string gapTopic,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gapTopic))
            return BadRequest(new { error = "gapTopic required" });
        var analysis = await _repo.GetSiteAnalysisAsync(id, ct);
        if (analysis is null) return NotFound();

        SiteAnalysisStoredPayload payload;
        try
        {
            payload = GccGenerateService.ParseAnalysisPayload(analysis.GapsJson);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }

        var section = GccGenerateService.TryBuildSectionContext(analysis.Id, payload, gapTopic);
        if (section is null || section.RelatedPages.Count == 0)
        {
            return UnprocessableEntity(new
            {
                error =
                    "No existing site pages in this section for the chosen gap. Site Analyzer Generate requires real related pages from the site model.",
            });
        }

        return Ok(section);
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
    public sealed record ToolsFromNamesRequest(
        IReadOnlyList<string>? ToolNames,
        string Brief,
        string? Provider);
    public sealed record SocialPackRequest(
        int FacebookCount = 0,
        int LinkedInCount = 0,
        int XCount = 0,
        int InstagramCount = 0,
        int MetaAdsCount = 0,
        int GoogleAdsCount = 0,
        string? Provider = null);
    public sealed record ProjectReviseRequest(
        string? ContentType,
        string Feedback,
        string? Scope,
        string? SectionPath,
        string? ToolSlug,
        string? Slug,
        string? Provider);
    public sealed record ImagePromptRequest(
        Guid? CreateId,
        string? Topic,
        string? Notes,
        Guid? SourceArtifactId,
        string? Provider);
    public sealed record ContentApprovalRequest(bool Approved = true);
    public sealed record AnalyzeSiteRequest(string Domain, string? SeedTopic = null);
}
