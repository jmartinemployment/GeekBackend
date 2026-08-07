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
        if (create.SiteAnalysisId is Guid analysisId && analysisId != Guid.Empty)
        {
            var analysis = await _repo.GetSiteAnalysisAsync(analysisId, ct);
            if (analysis is not null &&
                string.Equals(analysis.Status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                lastAnalyzedAtUtc = analysis.UpdatedAtUtc;
                analysisAgeDays = Math.Max(0, (int)(DateTime.UtcNow - analysis.UpdatedAtUtc).TotalDays);
                analysisStale = analysisAgeDays >= SiteAnalysisStaleAfterDays;
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
            create.SiteAnalysisId,
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
            sectionJson,
            Department: string.IsNullOrWhiteSpace(request.Department) ? "marketing" : request.Department.Trim()), ct);

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
        if (create.SiteAnalysisId is not Guid analysisId || analysisId == Guid.Empty)
            return null;

        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return null;

        var analysis = await _repo.GetSiteAnalysisAsync(analysisId, ct);
        if (analysis?.SeoProfileId is not Guid profileId)
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
        if (create.SiteAnalysisId is not Guid analysisId || analysisId == Guid.Empty)
            return null;

        var analysis = await _repo.GetSiteAnalysisAsync(analysisId, ct);
        if (analysis is null) return null;
        if (!string.Equals(analysis.Status, "ready", StringComparison.OrdinalIgnoreCase))
            return null;

        var ageDays = Math.Max(0, (int)(DateTime.UtcNow - analysis.UpdatedAtUtc).TotalDays);
        if (ageDays < SiteAnalysisStaleAfterDays)
            return null;

        return new
        {
            error = "stale_site_analysis",
            message =
                $"This site's analysis is {ageDays} day(s) old — re-analyze now, or proceed with stale grounding?",
            lastAnalyzedAtUtc = analysis.UpdatedAtUtc,
            analysisAgeDays = ageDays,
            staleAfterDays = SiteAnalysisStaleAfterDays,
            domain = analysis.Domain,
            siteAnalysisId = analysis.Id,
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

        var bodyJson = await gen.GenerateStartingContentAsync(create, section, provider, ct, mustMentionBlock);
        var primaryArtifact = await repo.CreateArtifactAsync(
            new CreateGccArtifactCommand(id, create.StartingContentType, create.Topic), ct);
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
    /// Starts (or re-runs) a Site Analysis for a domain. Domains aren't static, so this is not
    /// first-time-only: at most one <see cref="GccSiteAnalysisDto"/> row is kept per normalized
    /// domain — a bare call reuses an existing ready/processing analysis rather than starting a
    /// duplicate; <c>force: true</c> explicitly re-runs it in place (same row, fresh crawl),
    /// which is how a stale analysis gets refreshed. <c>lastAnalyzedAtUtc</c> in the response
    /// (set once a run reaches "ready") is the staleness signal the create-start UI/Generate use
    /// to decide whether to prompt for re-analysis — never decided silently here.
    /// </summary>
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
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required to run site analysis" });

        var normalizedDomain = HttpGeekSeoSiteAnalyzerClient.NormalizeHost(request.Domain);
        var existing = await _repo.GetLatestSiteAnalysisByDomainAsync(normalizedDomain, ct);

        // Version-aware reuse: pre-2.0 / orphaned rows (CreatedAtUtc < 2026-08-06) are treated as absent.
        // Cross-DB time cutoff is the proxy for AnalysisVersion != "2.0" since GccSiteAnalysis has no version column.
        var isStale = existing is not null && existing.CreatedAtUtc < new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        if (isStale) existing = null;

        // Content Creator Analyze always starts a new Geek-SEO crawl. Never return a cached
        // ready/processing row as if a scan happened — gaps without a scan are invalid.
        var projectResult = await _seo.EnsureProjectForDomainAsync(request.Domain, bearer, ct);
        if (!projectResult.Ok || projectResult.Value is null)
        {
            return StatusCode(
                projectResult.StatusCode is >= 400 and < 600 ? projectResult.StatusCode : 502,
                new { error = projectResult.Error ?? "Failed to ensure site analysis project" });
        }

        var project = projectResult.Value;
        var startResult = await _seo.StartSiteAnalysisAsync(
            project.Id, request.Domain, request.SeedTopic, bearer, ct);
        if (!startResult.Ok || startResult.Value == Guid.Empty)
        {
            return StatusCode(
                startResult.StatusCode is >= 400 and < 600 ? startResult.StatusCode : 502,
                new { error = startResult.Error ?? "Failed to start site analysis" });
        }

        var emptyPayload = new SiteAnalysisStoredPayload(
            [], [], [], startResult.Value, project.Id);
        var emptyGapsJson = GccGenerateService.SerializeAnalysisPayload(emptyPayload);
        var emptySiteModelJson = SerializeSiteModel([], []);

        GccSiteAnalysisDto persisted;
        if (existing is not null)
        {
            // Re-analyze in place: same row/Id, so a create's SiteAnalysisId keeps pointing at
            // one continuously-refreshed analysis per domain instead of an orphaned duplicate.
            persisted = await _repo.UpdateSiteAnalysisAsync(
                existing.Id,
                new UpdateGccSiteAnalysisCommand(
                    "processing", project.Id, startResult.Value, null, emptyGapsJson, emptySiteModelJson),
                ct);
        }
        else
        {
            persisted = await _repo.CreateSiteAnalysisAsync(
                new CreateGccSiteAnalysisCommand(
                    Id: null,
                    Domain: normalizedDomain,
                    SeedTopic: request.SeedTopic,
                    GapsJson: emptyGapsJson,
                    Status: "processing",
                    SeoProjectId: project.Id,
                    SeoProfileId: startResult.Value,
                    SiteModelJson: emptySiteModelJson),
                ct);
        }

        return Ok(ToAnalyzeSiteResponse(persisted));
    }

    private static object ToAnalyzeSiteResponse(GccSiteAnalysisDto analysis) => new
    {
        id = analysis.Id,
        domain = analysis.Domain,
        status = analysis.Status,
        seoProjectId = analysis.SeoProjectId,
        seoProfileId = analysis.SeoProfileId,
        lastAnalyzedAtUtc = string.Equals(analysis.Status, "ready", StringComparison.OrdinalIgnoreCase)
            ? analysis.UpdatedAtUtc
            : (DateTime?)null,
    };

    /// <summary>Workflow nav unlock: true when any Content Creator site analysis is ready.</summary>
    [HttpGet("site-analyzer/ready")]
    public async Task<IActionResult> SiteAnalyzerReady(CancellationToken ct)
    {
        var ready = await _repo.HasReadySiteAnalysisAsync(ct);
        return Ok(new { ready });
    }

    [HttpGet("site-analyzer/{id:guid}")]
    public async Task<IActionResult> GetSiteAnalysis(Guid id, CancellationToken ct)
    {
        var analysis = await _repo.GetSiteAnalysisAsync(id, ct);
        if (analysis is null) return NotFound();

        if (string.Equals(analysis.Status, "ready", StringComparison.OrdinalIgnoreCase))
        {
            var persistedFindings = await _repo.ListSiteFindingsAsync(analysis.Id, ct);
            var readyGaps = GccGenerateService.DeserializeGaps(analysis.GapsJson);
            // Fail-closed: never surface ready without content gaps.
            if (readyGaps.Count == 0)
            {
                analysis = await MarkAnalysisFailedAsync(
                    analysis,
                    "Site analysis is marked ready but has no content gaps.",
                    ct);
                return Ok(new { analysis.Id, analysis.Domain, analysis.Status, error = analysis.ErrorMessage });
            }

            return Ok(new
            {
                analysis.Id,
                analysis.Domain,
                analysis.Status,
                lastAnalyzedAtUtc = analysis.UpdatedAtUtc,
                gaps = readyGaps,
                findings = persistedFindings,
            });
        }

        // Missing/unknown status is not ready — continue poll / fail path below.
        if (string.IsNullOrWhiteSpace(analysis.Status))
            analysis = await MarkAnalysisFailedAsync(analysis, "Site analysis has no status.", ct);

        if (string.Equals(analysis.Status, "failed", StringComparison.OrdinalIgnoreCase))
            return Ok(new { analysis.Id, analysis.Domain, analysis.Status, error = analysis.ErrorMessage });

        if (analysis.CreatedAtUtc < DateTime.UtcNow.AddMinutes(-15))
        {
            analysis = await MarkAnalysisFailedAsync(analysis, "Site analysis timed out after 15 minutes.", ct);
            return Ok(new { analysis.Id, analysis.Domain, analysis.Status, error = analysis.ErrorMessage });
        }

        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required to load site analysis" });
        if (analysis.SeoProfileId is not Guid profileId || analysis.SeoProjectId is not Guid projectId)
        {
            analysis = await MarkAnalysisFailedAsync(analysis, "Site analysis is missing SEO profile or project IDs.", ct);
            return Ok(new { analysis.Id, analysis.Domain, analysis.Status, error = analysis.ErrorMessage });
        }

        var statusResult = await _seo.GetSiteAnalysisStatusAsync(profileId, bearer, ct);
        if (!statusResult.Ok || statusResult.Value is null)
            return StatusCode(statusResult.StatusCode is >= 400 and < 600 ? statusResult.StatusCode : 502,
                new { error = statusResult.Error ?? "Failed to poll site analysis" });

        var seoStatus = statusResult.Value;
        if (seoStatus.IsFailed)
        {
            analysis = await MarkAnalysisFailedAsync(
                analysis,
                seoStatus.ErrorMessage ?? "Site analysis failed in the SEO service.",
                ct);
            return Ok(new { analysis.Id, analysis.Domain, analysis.Status, error = analysis.ErrorMessage });
        }

        if (!seoStatus.IsComplete)
            return Ok(new
            {
                analysis.Id,
                analysis.Domain,
                analysis.Status,
                seoStatus = seoStatus.Status,
                step = seoStatus.Step,
                stepNumber = seoStatus.StepNumber,
                totalSteps = seoStatus.TotalSteps,
            });

        var modelResult = await _seo.LoadSiteModelByProfileAsync(projectId, profileId, analysis.Domain, bearer, ct);
        if (!modelResult.Ok || modelResult.Value is null)
        {
            analysis = await MarkAnalysisFailedAsync(analysis, modelResult.Error ?? "Failed to load completed site analysis.", ct);
            return Ok(new { analysis.Id, analysis.Domain, analysis.Status, error = analysis.ErrorMessage });
        }

        var snapshot = modelResult.Value;
        if (snapshot.Gaps.Count == 0 || snapshot.SitePages.Count == 0)
        {
            analysis = await MarkAnalysisFailedAsync(analysis, "Completed site analysis contained no gaps or existing pages.", ct);
            return Ok(new { analysis.Id, analysis.Domain, analysis.Status, error = analysis.ErrorMessage });
        }

        var gaps = snapshot.Gaps.Select(g => new ContentGapDto(g.Id, g.Topic, g.SectionPath, g.Reason, g.SuggestPillar)).ToList();
        var payload = new SiteAnalysisStoredPayload(
            gaps, snapshot.SitePages.ToList(), snapshot.TopicalNeighbors.ToList(), profileId, projectId);
        analysis = await _repo.UpdateSiteAnalysisAsync(
            analysis.Id,
            new UpdateGccSiteAnalysisCommand(
                "ready", projectId, profileId, null,
                GccGenerateService.SerializeAnalysisPayload(payload),
                SerializeSiteModel(snapshot.SitePages, snapshot.TopicalNeighbors)),
            ct);

        var findings = await _repo.ReplaceSiteFindingsAsync(
            analysis.Id,
            new CreateGccSiteFindingsCommand(CreateContentGapFindings(analysis.Id, gaps)),
            ct);

        return Ok(new
        {
            analysis.Id,
            analysis.Domain,
            analysis.Status,
            lastAnalyzedAtUtc = analysis.UpdatedAtUtc,
            gaps,
            findings,
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
        var analysis = await _repo.GetSiteAnalysisAsync(id, ct);
        if (analysis is null) return NotFound();
        if (analysis.SeoProfileId is not Guid profileId)
            return Conflict(new { error = "Site analysis is missing its SEO profile ID." });

        var bearer = GetBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required to download the sitemap." });

        var result = await _seo.GetSitemapXmlAsync(profileId, bearer, ct);
        if (!result.Ok || result.Value is null)
            return StatusCode(
                result.StatusCode is >= 400 and < 600 ? result.StatusCode : 502,
                new { error = result.Error ?? "Failed to load sitemap." });

        return Content(result.Value, "application/xml");
    }

    [HttpGet("site-analyzer/{id:guid}/gaps")]
    public async Task<IActionResult> Gaps(Guid id, CancellationToken ct)
    {
        var analysis = await _repo.GetSiteAnalysisAsync(id, ct);
        if (analysis is null) return NotFound();
        if (!string.Equals(analysis.Status, "ready", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "Site analysis is not ready.", status = analysis.Status });
        try
        {
            var gaps = GccGenerateService.DeserializeGaps(analysis.GapsJson);
            if (gaps.Count == 0)
                return Conflict(new { error = "Site analysis has no content gaps.", status = analysis.Status });
            return Ok(gaps);
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
        if (!string.Equals(analysis.Status, "ready", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "Site analysis is not ready.", status = analysis.Status });

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
            JsonSerializer.Serialize(new { sectionPath = g.SectionPath, suggestPillar = g.SuggestPillar }, JsonOpts),
            now)).ToList();
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
