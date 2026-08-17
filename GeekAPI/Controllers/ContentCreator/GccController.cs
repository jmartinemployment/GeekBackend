using System.Text.Json;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Infrastructure;
using GeekAPI.Services.Workflow.Infrastructure.InMemory;
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
    private readonly HttpGeekSeoSiteAnalyzerClient _seo;
    private readonly GccJobStore _jobs;
    private readonly ICurrentUserContext _user;
    private readonly ILogger<GccController> _logger;

    public GccController(
        HttpGccRepository repo,
        GccGenerateService gen,
        HttpGeekSeoSiteAnalyzerClient seo,
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

    /// <summary>
    /// Site grounding older than this many days requires an explicit operator choice before Generate
    /// proceeds — re-analyze now, or acknowledge stale grounding. Never silently proceed.
    /// </summary>
    public const int SiteAnalysisStaleAfterDays = 30;

    [HttpGet("creates/{id:guid}")]
    public async Task<ActionResult<object>> GetCreate(Guid id, CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();
        var artifacts = await _repo.ListArtifactsAsync(id, ct);

        DateTime? lastAnalyzedAtUtc = null;
        int? analysisAgeDays = null;
        bool analysisStale = false;
        if (create.SiteAnalysisId is Guid crawlId && crawlId != Guid.Empty)
        {
            var bearer = GetBearerToken();
            if (!string.IsNullOrWhiteSpace(bearer))
            {
                var statusResult = await _seo.GetSiteAnalysisStatusAsync(crawlId, bearer, ct);
                if (statusResult.Ok && statusResult.Value is { } seoStatus && seoStatus.IsComplete)
                {
                    var at = (seoStatus.ProgressAt ?? seoStatus.CreatedAt)?.UtcDateTime;
                    if (at is DateTime analyzedAt)
                    {
                        lastAnalyzedAtUtc = analyzedAt;
                        analysisAgeDays = Math.Max(0, (int)(DateTime.UtcNow - analyzedAt).TotalDays);
                        analysisStale = analysisAgeDays >= SiteAnalysisStaleAfterDays;
                    }
                }
            }
        }

        return Ok(new
        {
            create.Id,
            create.ClientId,
            create.OwnerUserId,
            create.StartingContentType,
            create.Topic,
            create.Notes,
            create.Department,
            siteAnalysisProfileId = create.SiteAnalysisId,
            create.SiteSectionJson,
            create.BriefJson,
            create.ResearchJson,
            create.Status,
            create.CreatedAtUtc,
            create.UpdatedAtUtc,
            lastAnalyzedAtUtc,
            analysisAgeDays,
            analysisStale,
            artifacts,
        });
    }

    [HttpPatch("creates/{id:guid}/brief-research")]
    public async Task<ActionResult<GccCreateDto>> UpdateBriefResearch(
        Guid id,
        [FromBody] UpdateBriefResearchRequest request,
        CancellationToken ct)
    {
        if (request is null) return BadRequest("Body required");
        if (request.BriefJson is null && request.ResearchJson is null)
            return BadRequest("briefJson and/or researchJson required");

        var existing = await _repo.GetCreateAsync(id, ct);
        if (existing is null) return NotFound();

        try
        {
            var updated = await _repo.UpdateBriefResearchAsync(
                id,
                new UpdateGccCreateBriefResearchCommand(request.BriefJson, request.ResearchJson),
                ct);
            return Ok(updated);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Update brief/research failed");
            return StatusCode(502, "Failed to persist brief/research");
        }
    }

    /// <summary>
    /// CWv2-style file upload: uploading IS the research action (no follow/process button).
    /// KeywordResult (saved Google SERP HTML) is parsed with <see cref="GccSavedSerpParser"/> into
    /// organics + related searches (PAA is parsed but always discarded — stays a manual brief
    /// field). Wiki/.edu/.gov are parsed as articles into quoteables, which Generate already reads.
    /// PeopleAlsoAsk .txt is parsed and returned for operator weeding, not dumped. Unlimited files.
    /// </summary>
    [HttpPost("creates/{id:guid}/keyword-sources")]
    public async Task<ActionResult<object>> UploadKeywordSource(
        Guid id,
        [FromForm] IFormFile? file,
        [FromForm] string? category,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("file required");
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();
        var cat = string.IsNullOrWhiteSpace(category) ? "KeywordResult" : category.Trim();

        string content;
        using (var reader = new StreamReader(file.OpenReadStream()))
            content = await reader.ReadToEndAsync(ct);

        // PeopleAlsoAsk: parse questions and return for weeding — the operator curates which
        // seed the brief (client persists selected into brief.paaQuestions). Not auto-dumped.
        if (string.Equals(cat, "PeopleAlsoAsk", StringComparison.OrdinalIgnoreCase))
        {
            var questions = content
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimStart('-', '*', '•', ' ').Trim())
                .Where(l => l.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Ok(new { category = cat, fileName = file.FileName, questions });
        }

        var sourceId = Guid.NewGuid().ToString("N");
        var existing = GccResearchFetchService.Deserialize(create.ResearchJson)
            ?? new GccResearchDocument(null, []);

        if (string.Equals(cat, "KeywordResult", StringComparison.OrdinalIgnoreCase))
        {
            // Saved Google SERP page → GccSavedSerpParser. Never hard-fails: even a zero-organic
            // parse is persisted with its ParseWarning, so a partial save isn't lost.
            var parsed = GccSavedSerpParser.Parse(content, create.Topic);
            var serpPage = new GccParsedSerpPage(
                sourceId, file.FileName, parsed.Organics, parsed.RelatedSearches, parsed.Shape, parsed.ParseWarning);

            var serpPages = (existing.SerpPages ?? []).ToList();
            serpPages.Add(serpPage);
            var srcMeta = new GccKeywordSource(sourceId, file.FileName, cat, 0, 0, 0);
            var sourcesList = (existing.Sources ?? []).ToList();
            sourcesList.Add(srcMeta);

            var serpJson = GccResearchFetchService.Serialize(
                existing with { SerpPages = serpPages, Sources = sourcesList });
            try
            {
                await _repo.UpdateBriefResearchAsync(
                    id, new UpdateGccCreateBriefResearchCommand(BriefJson: null, ResearchJson: serpJson), ct);
                return Ok(new GccKeywordSourceDetail(
                    srcMeta.Id, srcMeta.FileName, srcMeta.Category, 0, 0, 0, serpPage));
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Persist uploaded keyword SERP failed");
                return StatusCode(502, "Failed to persist uploaded research");
            }
        }

        // Wiki/.edu/.gov: unchanged article path → quoteable (unlimited; no cap).
        var page = GccArticleHtmlExtractor.Extract($"upload://{sourceId}/{file.FileName}", content);
        if (GccArticleHtmlExtractor.IsEmpty(page))
            return BadRequest(
                "No article headings or paragraphs found. This upload expects saved article HTML (Wikipedia / .edu / .gov) with h1–h6 and <p> text.");

        var quoteables = existing.Quoteables.ToList();
        quoteables.Add(page);
        var sources = (existing.Sources ?? []).ToList();
        var src = new GccKeywordSource(
            sourceId, file.FileName, cat, page.Headings.Count, page.Paragraphs.Count, 0);
        sources.Add(src);

        var json = GccResearchFetchService.Serialize(
            existing with { Quoteables = quoteables, Sources = sources });
        try
        {
            await _repo.UpdateBriefResearchAsync(
                id, new UpdateGccCreateBriefResearchCommand(BriefJson: null, ResearchJson: json), ct);
            return Ok(new GccKeywordSourceDetail(
                src.Id, src.FileName, src.Category, src.HeadingCount, src.ParagraphCount, src.QuestionCount, null));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Persist uploaded keyword source failed");
            return StatusCode(502, "Failed to persist uploaded research");
        }
    }

    [HttpGet("creates/{id:guid}/keyword-sources")]
    public async Task<ActionResult<IReadOnlyList<GccKeywordSourceDetail>>> ListKeywordSources(
        Guid id, CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();
        var doc = GccResearchFetchService.Deserialize(create.ResearchJson);
        var sources = doc?.Sources ?? [];
        var pagesById = (doc?.SerpPages ?? []).ToDictionary(p => p.Id);
        var result = sources
            .Select(s => new GccKeywordSourceDetail(
                s.Id, s.FileName, s.Category, s.HeadingCount, s.ParagraphCount, s.QuestionCount,
                pagesById.GetValueOrDefault(s.Id)))
            .ToList();
        return Ok((IReadOnlyList<GccKeywordSourceDetail>)result);
    }

    [HttpDelete("creates/{id:guid}/keyword-sources/{sourceId}")]
    public async Task<IActionResult> DeleteKeywordSource(Guid id, string sourceId, CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();
        var doc = GccResearchFetchService.Deserialize(create.ResearchJson);
        if (doc is null) return NoContent();

        var prefix = $"upload://{sourceId}/";
        var quoteables = doc.Quoteables
            .Where(q => !q.Url.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        var serpPages = (doc.SerpPages ?? []).Where(p => p.Id != sourceId).ToList();
        var sources = (doc.Sources ?? []).Where(s => s.Id != sourceId).ToList();
        var json = GccResearchFetchService.Serialize(
            doc with { Quoteables = quoteables, SerpPages = serpPages, Sources = sources });
        await _repo.UpdateBriefResearchAsync(
            id, new UpdateGccCreateBriefResearchCommand(BriefJson: null, ResearchJson: json), ct);
        return NoContent();
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
            if (request.SiteAnalysisProfileId is Guid aid && aid != Guid.Empty
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
            request.SiteAnalysisProfileId,
            sectionJson,
            Department: string.IsNullOrWhiteSpace(request.Department) ? "marketing" : request.Department.Trim()), ct);

        return CreatedAtAction(nameof(GetCreate), new { id = created.Id }, created);
    }

    [HttpGet("clients/{id:guid}")]
    public async Task<ActionResult<GccClientDto>> GetClient(Guid id, CancellationToken ct)
    {
        var client = await _repo.GetClientByIdAsync(id, ct);
        if (client is null) return NotFound();
        return Ok(client);
    }

    [HttpGet("clients")]
    public async Task<ActionResult<GccClientDto>> GetClientByName([FromQuery] string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("name query parameter is required");

        var client = await _repo.GetClientByNameAsync(name, ct);
        if (client is null) return NotFound();
        return Ok(client);
    }

    [HttpPost("clients")]
    public async Task<ActionResult<GccClientDto>> CreateClient([FromBody] CreateGccClientCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        if (string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("Name is required");

        var client = await _repo.CreateClientAsync(command, ct);
        return CreatedAtAction(nameof(GetClient), new { id = client.Id }, client);
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
            GccGenerateService.ValidateBriefRequired(create);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        if (!TryParseProvider(request?.Provider, out var provider, out var err))
            return BadRequest(err);

        // Staleness is an operator choice shown before Generate runs — never a silent proceed.
        var staleGate = await TryBuildStaleGroundingResponseAsync(
            create, request?.AcknowledgeStaleGrounding == true, ct);
        if (staleGate is not null)
            return Conflict(staleGate);

        var mustMentionBlock = await TryBuildMustMentionBlockAsync(create, ct);

        try
        {
            var result = await RunGenerateAsync(_repo, _gen, create, section, provider, request?.OutputTypes, mustMentionBlock, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("brief required", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Site Analyzer", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(ex.Message);
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

    private static readonly HashSet<string> LongFormTypes =
        new(StringComparer.OrdinalIgnoreCase) { "pillar", "blog", "techArticle" };

    /// <summary>
    /// Looks up this create's real "must mention" sub-topics from its analyzed site's persisted
    /// page-section trees (see GccGenerateService.BuildMustMentionSubtopicsBlock). Returns null
    /// (no injection, no failure) when there's no attached analysis, no bearer token, or no
    /// deterministic slug match — a missing/uncertain match must never block Generate or inject
    /// a guessed subtree.
    /// </summary>
    private async Task<string?> TryBuildMustMentionBlockAsync(GccCreateDto create, CancellationToken ct)
    {
        if (create.SiteAnalysisId is not Guid profileId || profileId == Guid.Empty)
            return null;

        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return null;

        var treesResult = await _seo.GetPageSectionTreesAsync(profileId, bearer, ct);
        if (!treesResult.Ok || treesResult.Value is null || treesResult.Value.Count == 0)
            return null;

        var block = GccGenerateService.BuildMustMentionSubtopicsBlock(treesResult.Value, create.Topic);
        return string.IsNullOrWhiteSpace(block) ? null : block;
    }

    /// <summary>
    /// When the create's site analysis is older than <see cref="SiteAnalysisStaleAfterDays"/> and
    /// the operator has not acknowledged stale grounding, returns a Conflict payload presenting
    /// the choice. Null means Generate may proceed (no analysis, not stale, or acknowledged).
    /// </summary>
    private async Task<object?> TryBuildStaleGroundingResponseAsync(
        GccCreateDto create,
        bool acknowledged,
        CancellationToken ct)
    {
        if (acknowledged) return null;
        if (create.SiteAnalysisId is not Guid profileId || profileId == Guid.Empty)
            return null;

        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return null;

        var statusResult = await _seo.GetSiteAnalysisStatusAsync(profileId, bearer, ct);
        if (!statusResult.Ok || statusResult.Value is null || !statusResult.Value.IsComplete)
            return null;

        var at = (statusResult.Value.ProgressAt ?? statusResult.Value.CreatedAt)?.UtcDateTime;
        if (at is null)
            return null;
        var ageDays = Math.Max(0, (int)(DateTime.UtcNow - at.Value).TotalDays);
        if (ageDays < SiteAnalysisStaleAfterDays)
            return null;

        return new
        {
            error = "stale_site_analysis",
            message =
                $"This site's analysis is {ageDays} day(s) old — re-analyze now, or proceed with stale grounding?",
            lastAnalyzedAtUtc = at,
            analysisAgeDays = ageDays,
            staleAfterDays = SiteAnalysisStaleAfterDays,
            siteAnalysisProfileId = profileId,
        };
    }

    private string? GetBearerToken()
    {
        var auth = Request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? auth["Bearer ".Length..].Trim()
            : null;
    }

    private async Task<object> RunGenerateAsync(
        HttpGccRepository repo,
        GccGenerateService gen,
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        IReadOnlyList<string>? outputTypes,
        string? mustMentionBlock,
        CancellationToken ct)
    {
        var requested = (outputTypes ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Multi-output: one long-form primary + derivatives, all persisted as artifacts.
        if (requested.Count > 1)
            return await RunMultiGenerateAsync(repo, gen, create, section, provider, requested, mustMentionBlock, ct);

        var id = create.Id;
        var contentType = (requested.Count == 1 ? requested[0] : create.StartingContentType).ToLowerInvariant();

        // Route to appropriate generator based on content type
        string bodyJson;
        switch (contentType)
        {
            case "pillar":
                bodyJson = await gen.GeneratePillarBodyAsync(create, section, provider, mustMentionBlock, ct);
                // Generate per-H2 image prompts
                var pillarImagePrompts = await gen.GenerateSectionImagePromptsAsync(
                    "pillar", create.Topic, bodyJson, section, provider, ct);
                break;

            case "blog":
                bodyJson = await gen.GenerateBlogBodyAsync(create, section, provider, mustMentionBlock, ct);
                // Generate per-H2 image prompts
                var blogImagePrompts = await gen.GenerateSectionImagePromptsAsync(
                    "blog", create.Topic, bodyJson, section, provider, ct);
                break;

            case "email":
                bodyJson = await gen.GenerateEmailAsync(create, section, provider, mustMentionBlock, ct);
                // Email gets one standalone image prompt
                bodyJson = await AddImagePromptForContentAsync(gen, "email", create.Topic, bodyJson, section, provider, ct);
                break;

            case "linkedin":
                bodyJson = await gen.GenerateSocialPostAsync(create, "linkedin", section, provider, mustMentionBlock, ct);
                // LinkedIn gets one standalone image prompt
                bodyJson = await AddImagePromptForContentAsync(gen, "linkedin", create.Topic, bodyJson, section, provider, ct);
                break;

            case "facebook":
                bodyJson = await gen.GenerateSocialPostAsync(create, "facebook", section, provider, mustMentionBlock, ct);
                // Facebook gets one standalone image prompt
                bodyJson = await AddImagePromptForContentAsync(gen, "facebook", create.Topic, bodyJson, section, provider, ct);
                break;

            default:
                // Fallback to old generic method for unsupported types
                bodyJson = await gen.GenerateStartingContentAsync(create, section, provider, ct, mustMentionBlock);
                break;
        }

        var primaryArtifact = await repo.CreateArtifactAsync(
            new CreateGccArtifactCommand(id, contentType, create.Topic), ct);
        var primaryVersion = await repo.CreateVersionAsync(
            new CreateGccArtifactVersionCommand(primaryArtifact.Id, bodyJson), ct);
        return new { artifact = primaryArtifact, version = primaryVersion };
    }

    /// <summary>
    /// Multi-output generate: produce one long-form primary body, then derive every other
    /// requested content type from it (email/social/ads via the repurpose-pack engine,
    /// additional long-form via revise-rewrite, image prompts + tools directly). No content
    /// approval required — derivatives run from the freshly generated body.
    /// </summary>
    private async Task<object> RunMultiGenerateAsync(
        HttpGccRepository repo,
        GccGenerateService gen,
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        IReadOnlyList<string> requested,
        string? mustMentionBlock,
        CancellationToken ct)
    {
        var id = create.Id;
        var created = new List<object>();

        // Primary = first long-form requested (else the create's starting type). Its body seeds
        // every document-derived derivative, so it must be a long-form document.
        var primaryType = requested.FirstOrDefault(LongFormTypes.Contains) ?? create.StartingContentType;
        var bodyJson = await gen.GenerateStartingContentAsync(create, section, provider, ct, mustMentionBlock);
        var primaryArtifact = await repo.CreateArtifactAsync(
            new CreateGccArtifactCommand(id, primaryType, create.Topic), ct);
        var primaryVersion = await repo.CreateVersionAsync(
            new CreateGccArtifactVersionCommand(primaryArtifact.Id, bodyJson), ct);
        created.Add(new { artifact = primaryArtifact, version = primaryVersion });

        var primaryIsDocument = LongFormTypes.Contains(primaryType);
        var packChannels = new List<string>();
        var emailIndex = 0;

        foreach (var type in requested)
        {
            if (string.Equals(type, primaryType, StringComparison.OrdinalIgnoreCase))
                continue;

            switch (type.ToLowerInvariant())
            {
                case "linkedin": packChannels.Add("LinkedIn"); break;
                case "x": packChannels.Add("X"); break;
                case "instagram": packChannels.Add("Instagram"); break;
                case "metaads": packChannels.Add("MetaAds"); break;
                case "googleads": packChannels.Add("GoogleAds"); break;

                case "email" when primaryIsDocument:
                {
                    var emailBody = await gen.GenerateRepurposePackAsync(bodyJson, ["Email"], provider, ct);
                    var a = await repo.CreateArtifactAsync(
                        new CreateGccArtifactCommand(id, "email", $"Email {++emailIndex}"), ct);
                    var v = await repo.CreateVersionAsync(
                        new CreateGccArtifactVersionCommand(a.Id, emailBody), ct);
                    created.Add(new { artifact = a, version = v });
                    break;
                }

                case "pillar" or "blog" or "techarticle" when primaryIsDocument:
                {
                    var rewritten = await gen.ReviseAsync(
                        bodyJson, $"Rewrite as a standalone {type}.", "full", null, provider, ct);
                    var a = await repo.CreateArtifactAsync(
                        new CreateGccArtifactCommand(id, type, $"{create.Topic} — {type}"), ct);
                    var v = await repo.CreateVersionAsync(
                        new CreateGccArtifactVersionCommand(a.Id, rewritten), ct);
                    created.Add(new { artifact = a, version = v });
                    break;
                }

                case "imageprompt":
                {
                    var promptJson = await gen.GenerateImagePromptJsonAsync(
                        create.Topic, create.Notes, primaryIsDocument ? bodyJson : null, provider, ct);
                    var a = await repo.CreateArtifactAsync(
                        new CreateGccArtifactCommand(id, "imagePrompt", $"{create.Topic} — Image prompt"), ct);
                    var v = await repo.CreateVersionAsync(
                        new CreateGccArtifactVersionCommand(a.Id, promptJson), ct);
                    created.Add(new { artifact = a, version = v });
                    break;
                }

                case "aitool":
                {
                    var (toolName, document, _, _) = await gen.GenerateToolAsync(
                        create.Topic, create.Notes, primaryIsDocument ? bodyJson : null, provider, ct);
                    var a = await repo.CreateArtifactAsync(
                        new CreateGccArtifactCommand(id, "aiTool", toolName), ct);
                    var v = await repo.CreateVersionAsync(
                        new CreateGccArtifactVersionCommand(a.Id, GccGenerateService.SerializeDocument(document)), ct);
                    created.Add(new { artifact = a, version = v });
                    break;
                }
            }
        }

        if (packChannels.Count > 0 && primaryIsDocument)
        {
            var packJson = await gen.GenerateRepurposePackAsync(bodyJson, packChannels, provider, ct);
            var a = await repo.CreateArtifactAsync(
                new CreateGccArtifactCommand(id, "socialPack", "Social / ads pack"), ct);
            var v = await repo.CreateVersionAsync(
                new CreateGccArtifactVersionCommand(a.Id, packJson), ct);
            created.Add(new { artifact = a, version = v });
        }

        return new { created };
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

    /// <summary>
    /// Starts a Through Coverage crawl. Progress arrives on the SEO SignalR hub; when complete
    /// the event includes <c>site_analysis_profiles.Id</c>. Nothing is written to gcc_site_analyses.
    /// </summary>
    [HttpPost("site-analyzer/analyze")]
    public async Task<IActionResult> AnalyzeSite(
        [FromBody] AnalyzeSiteRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Domain))
            return BadRequest(new { error = "domain required" });

        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required to run site analysis" });

        var projectResult = await _seo.EnsureProjectForDomainAsync(request.Domain, bearer, ct);
        if (!projectResult.Ok || projectResult.Value is null)
        {
            return StatusCode(
                projectResult.StatusCode is >= 400 and < 600 ? projectResult.StatusCode : 502,
                new { error = projectResult.Error ?? "Failed to ensure site analysis project" });
        }

        var startResult = await _seo.StartSiteAnalysisAsync(
            projectResult.Value.Id, request.Domain, request.SeedTopic, bearer, ct);
        if (!startResult.Ok)
        {
            return StatusCode(
                startResult.StatusCode is >= 400 and < 600 ? startResult.StatusCode : 502,
                new { error = startResult.Error ?? "Failed to start site analysis" });
        }

        return Ok(new { status = "queued" });
    }

    /// <summary>Workflow nav unlock: true when this user has a completed crawl.</summary>
    [HttpGet("site-analyzer/ready")]
    public async Task<IActionResult> SiteAnalyzerReady(CancellationToken ct)
    {
        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required" });
        var list = await _seo.ListRecentProfilesAsync(bearer, 20, ct);
        if (!list.Ok)
            return StatusCode(list.StatusCode is >= 400 and < 600 ? list.StatusCode : 502, new { error = list.Error });
        var ready = (list.Value ?? []).Any(p =>
            string.Equals(p.Status, "complete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Status, "ready", StringComparison.OrdinalIgnoreCase));
        return Ok(new { ready });
    }

    /// <summary>Load pages and gaps for a finished crawl (<c>site_analysis_profiles.Id</c>).</summary>
    [HttpGet("site-analyzer/{id:guid}")]
    public Task<IActionResult> GetSiteAnalysis(Guid id, CancellationToken ct) =>
        SnapshotByProfileIdAsync(id, ct);

    private async Task<IActionResult> SnapshotByProfileIdAsync(Guid siteAnalysisProfileId, CancellationToken ct)
    {
        if (siteAnalysisProfileId == Guid.Empty)
            return BadRequest(new { error = "siteAnalysisProfileId required" });
        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required to load site analysis" });

        var model = await _seo.LoadSiteModelByProfileAsync(
            Guid.Empty, siteAnalysisProfileId, "", bearer, ct);
        if (!model.Ok || model.Value is null)
            return StatusCode(
                model.StatusCode is >= 400 and < 600 ? model.StatusCode : 502,
                new { error = model.Error ?? "Failed to load crawl" });

        var snapshot = model.Value;
        var gaps = snapshot.Gaps.Select(g => new ContentGapDto(
            g.Id, g.Topic, g.SectionPath, g.Reason, g.Hierarchy, g.SourcePageUrl)).ToList();
        var pages = snapshot.SitePages
            .Select(sp => new
            {
                url = sp.Url,
                title = sp.Title,
                headings = (sp.Headings ?? Enumerable.Empty<HeadingDto>())
                    .Select(h => new { level = h.Level, text = h.Text })
                    .ToArray()
            })
            .ToList();

        return Ok(new
        {
            siteAnalysisProfileId,
            status = "ready",
            domain = snapshot.Domain,
            gaps,
            pages,
        });
    }

    /// <summary>
    /// Downloads the generated sitemap.xml for this Site Analyzer run. Delegates to Geek-SEO,
    /// which rebuilds the document from the current step-1 URL inventory on every request — there
    /// is no separate "stale artifact" to go out of sync with the latest Analyze. No dedicated
    /// Sitemap page, no FTP/root upload — download only.
    /// </summary>
    [HttpGet("site-analyzer/{id:guid}/sitemap")]
    public async Task<IActionResult> SitemapXml(Guid id, CancellationToken ct)
    {
        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required to download the sitemap." });

        var result = await _seo.GetSitemapXmlAsync(id, bearer, ct);
        if (!result.Ok || result.Value is null)
            return StatusCode(
                result.StatusCode is >= 400 and < 600 ? result.StatusCode : 502,
                new { error = result.Error ?? "Failed to load sitemap." });

        return Content(result.Value, "application/xml");
    }

    [HttpGet("site-analyzer/{id:guid}/gaps")]
    public async Task<IActionResult> Gaps(Guid id, CancellationToken ct)
    {
        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required" });
        var model = await _seo.LoadSiteModelByProfileAsync(Guid.Empty, id, "", bearer, ct);
        if (!model.Ok || model.Value is null)
            return StatusCode(
                model.StatusCode is >= 400 and < 600 ? model.StatusCode : 502,
                new { error = model.Error ?? "Failed to load crawl" });
        var gaps = model.Value.Gaps.Select(g => new ContentGapDto(
            g.Id, g.Topic, g.SectionPath, g.Reason, g.Hierarchy, g.SourcePageUrl)).ToList();
        return Ok(gaps);
    }

    /// <summary>
    /// Legacy name: returns PageContext (headings + markdown), NOT nested trees.
    /// Prefer hierarchy-match / page-contexts for new callers.
    /// </summary>
    [HttpGet("site-analyzer/{id:guid}/page-section-trees")]
    public async Task<IActionResult> PageSectionTrees(Guid id, CancellationToken ct)
    {
        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required to load page contexts" });

        var contexts = await _seo.GetPageContextsAsync(id, bearer, ct);
        if (!contexts.Ok)
            return StatusCode(contexts.StatusCode, new { error = contexts.Error });

        var payload = (contexts.Value ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.PageUrl))
            .Select(p => new
            {
                pageUrl = p.PageUrl,
                title = p.Title ?? "",
                description = p.Description ?? "",
                headings = p.Headings ?? [],
                markdown = p.Markdown ?? "",
            })
            .ToList();

        return Ok(payload);
    }

    /// <summary>Recent site_analysis_profiles.Id rows for Site Analyzer picker.</summary>
    [HttpGet("site-analyzer/profiles/recent")]
    public async Task<IActionResult> ListRecentSiteAnalysisProfiles(
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required" });

        var result = await _seo.ListRecentProfilesAsync(bearer, limit, ct);
        if (!result.Ok)
            return StatusCode(result.StatusCode, new { error = result.Error });
        return Ok(result.Value ?? []);
    }

    /// <summary>site_analysis_profiles for a normalized domain host.</summary>
    [HttpGet("site-analyzer/profiles/by-domain")]
    public async Task<IActionResult> ListSiteAnalysisProfilesByDomain(
        [FromQuery] string domain,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return BadRequest(new { error = "domain required" });
        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required" });

        var result = await _seo.ListProfilesByDomainAsync(domain, bearer, limit, ct);
        if (!result.Ok)
            return StatusCode(result.StatusCode, new { error = result.Error });
        return Ok(result.Value ?? []);
    }

    /// <summary>
    /// SQL hierarchy match for site_analysis_profiles.Id + keyword.
    /// Returns ranked matches (path, children, assignment) or empty array.
    /// </summary>
    [HttpGet("site-analyzer/profiles/{siteAnalysisProfileId:guid}/hierarchy-match")]
    public async Task<IActionResult> HierarchyMatchBySiteAnalysisProfileId(
        Guid siteAnalysisProfileId,
        [FromQuery] string keyword,
        CancellationToken ct)
    {
        if (siteAnalysisProfileId == Guid.Empty)
            return BadRequest(new { error = "siteAnalysisProfileId required" });
        if (string.IsNullOrWhiteSpace(keyword))
            return BadRequest(new { error = "keyword required" });

        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required" });

        var trees = await _seo.FindTreesByKeywordAsync(siteAnalysisProfileId, keyword.Trim(), bearer, ct);
        if (!trees.Ok)
            return StatusCode(trees.StatusCode, new { error = trees.Error });

        var matches = GccGenerateService.BuildHierarchyMatchesFromTrees(trees.Value ?? [], keyword.Trim());
        return Ok(matches);
    }

    /// <summary>Page contexts for a site_analysis_profiles.Id (may be empty ContextJson).</summary>
    [HttpGet("site-analyzer/profiles/{siteAnalysisProfileId:guid}/page-contexts")]
    public async Task<IActionResult> PageContextsBySiteAnalysisProfileId(
        Guid siteAnalysisProfileId,
        CancellationToken ct)
    {
        if (siteAnalysisProfileId == Guid.Empty)
            return BadRequest(new { error = "siteAnalysisProfileId required" });
        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required" });

        var contexts = await _seo.GetPageContextsAsync(siteAnalysisProfileId, bearer, ct);
        if (!contexts.Ok)
            return StatusCode(contexts.StatusCode, new { error = contexts.Error });

        return Ok((contexts.Value ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.PageUrl))
            .Select(p => new
            {
                pageUrl = p.PageUrl,
                title = p.Title ?? "",
                description = p.Description ?? "",
                headings = p.Headings ?? [],
                markdown = p.Markdown ?? "",
            }));
    }

    /// <summary>Real nested trees for site_analysis_profiles.Id (TreeJson), for reports / grounding.</summary>
    [HttpGet("site-analyzer/profiles/{siteAnalysisProfileId:guid}/trees")]
    public async Task<IActionResult> TreesBySiteAnalysisProfileId(
        Guid siteAnalysisProfileId,
        CancellationToken ct)
    {
        if (siteAnalysisProfileId == Guid.Empty)
            return BadRequest(new { error = "siteAnalysisProfileId required" });
        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required" });

        var trees = await _seo.GetPageSectionTreesAsync(siteAnalysisProfileId, bearer, ct);
        if (!trees.Ok)
            return StatusCode(trees.StatusCode, new { error = trees.Error });

        return Ok((trees.Value ?? []).Select(t => new
        {
            pageUrl = t.PageUrl,
            treeJson = t.TreeJson,
            siteAnalysisProfileId = t.SiteAnalysisProfileId,
        }));
    }

    [HttpGet("site-analyzer/{id:guid}/section-context")]
    public async Task<IActionResult> SectionContext(
        Guid id,
        [FromQuery] string gapTopic,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gapTopic))
            return BadRequest(new { error = "gapTopic required" });
        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required" });

        var model = await _seo.LoadSiteModelByProfileAsync(Guid.Empty, id, "", bearer, ct);
        if (!model.Ok || model.Value is null)
            return StatusCode(
                model.StatusCode is >= 400 and < 600 ? model.StatusCode : 502,
                new { error = model.Error ?? "Failed to load crawl" });

        var snapshot = model.Value;
        var gaps = snapshot.Gaps.Select(g => new ContentGapDto(
            g.Id, g.Topic, g.SectionPath, g.Reason, g.Hierarchy, g.SourcePageUrl)).ToList();
        var payload = new SiteAnalysisStoredPayload(
            gaps, snapshot.SitePages.ToList(), snapshot.TopicalNeighbors.ToList(), id, snapshot.SeoProjectId);

        var section = GccGenerateService.TryBuildSectionContext(id, payload, gapTopic);
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

    /// <summary>
    /// Parse an operator-saved Google results page (HTML or text) into organics, PAA, related, and SERP shape.
    /// Primary SERP path for Content Creator — not a live scrape / paid vendor.
    /// </summary>
    [HttpPost("serp/parse")]
    public IActionResult ParseSavedSerp([FromBody] ParseSavedSerpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "content required (saved Google results HTML or text)" });

        var parsed = GccSavedSerpParser.Parse(request.Content, request.TargetKeyword);
        return Ok(parsed);
    }

    private async Task<GccSiteAnalysisDto> MarkAnalysisFailedAsync(
        GccSiteAnalysisDto analysis,
        string error,
        CancellationToken ct) =>
        await _repo.UpdateSiteAnalysisAsync(
            analysis.Id,
            new UpdateGccSiteAnalysisCommand(
                "failed",
                analysis.SeoProjectId,
                analysis.SeoProfileId,
                error,
                null,
                null),
            ct);

    private static string SerializeSiteModel(
        IReadOnlyList<RelatedPageDto> sitePages,
        IReadOnlyList<string> topicalNeighbors) =>
        JsonSerializer.Serialize(new { sitePages, topicalNeighbors }, JsonOpts);

    private static IReadOnlyList<GccSiteFindingDto> CreateContentGapFindings(
        Guid analysisId,
        IReadOnlyList<ContentGapDto> gaps)
    {
        var now = DateTime.UtcNow;
        return gaps.Select(g => new GccSiteFindingDto(
            Guid.NewGuid(),
            analysisId,
            "content_gap",
            g.Reason.Contains("quick-win", StringComparison.OrdinalIgnoreCase) ? "warning" : "info",
            null,
            g.Topic,
            g.Reason,
            JsonSerializer.Serialize(new
            {
                sectionPath = g.SectionPath,
                hierarchy = g.Hierarchy,
                sourcePageUrl = g.SourcePageUrl,
            }, JsonOpts),
            now)).ToList();
    }

    private async Task<string> AddImagePromptForContentAsync(
        GccGenerateService gen,
        string contentType,
        string topic,
        string contentJson,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        try
        {
            var imagePromptJson = await gen.GenerateImagePromptJsonAsync(
                topic, null, contentJson, provider, ct);
            // For now, just return the content as-is; image prompts can be stored separately
            return contentJson;
        }
        catch
        {
            // Image prompt generation is optional, don't fail the whole generation
            return contentJson;
        }
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
        Guid? SiteAnalysisProfileId,
        SiteSectionContextDto? SiteSection,
        string? Department = null);

    public sealed record ProviderRequest(
        string? Provider,
        bool Async = false,
        IReadOnlyList<string>? OutputTypes = null,
        bool AcknowledgeStaleGrounding = false);
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
    public sealed record AnalyzeSiteRequest(string Domain, string? SeedTopic = null, bool Force = false);
    public sealed record UpdateBriefResearchRequest(string? BriefJson, string? ResearchJson);
    public sealed record ParseSavedSerpRequest(string Content, string? TargetKeyword = null);
}
