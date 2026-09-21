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
// Types this controller reads moved during the v2 namespace migration (582a171); the controller was
// deleted in the same commit, so it never saw the move.
// RelatedPageDto / SiteSectionContextDto / ContentGapDto only. v1 no longer calls any V2 service —
// these are shared data shapes that happen to sit in a V2 file. Moving them to a neutral namespace
// would touch V2, which is deliberately left alone.
using GeekAPI.Services.ContentCreatorV2;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;
using GeekAPI.Services.GeekSeo;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Authorization;
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
    private readonly IProjectStore _projects;
    private readonly IContentGenerationOrchestrator _orchestrator;
    private readonly CompanyProfileOptions _company;
    private readonly GccGenerateService _gen;
    private readonly GccGroundingResolver _grounding;
    private readonly HttpGeekSeoSiteAnalyzerClient _seo;
    private readonly GccJobStore _jobs;
    private readonly ICurrentUserContext _user;
    private readonly HttpGeekCrawlerRepository _crawlerRepo;
    private readonly IGeekCrawlerRagClient _rag;
    private readonly ILogger<GccController> _logger;

    public GccController(
        HttpGccRepository repo,
        IProjectStore projects,
        IContentGenerationOrchestrator orchestrator,
        IOptions<CompanyProfileOptions> company,
        GccGenerateService gen,
        GccGroundingResolver grounding,
        HttpGeekSeoSiteAnalyzerClient seo,
        GccJobStore jobs,
        ICurrentUserContext user,
        HttpGeekCrawlerRepository crawlerRepo,
        IGeekCrawlerRagClient rag,
        ILogger<GccController> logger)
    {
        _repo = repo;
        _projects = projects;
        _orchestrator = orchestrator;
        _company = company.Value;
        _gen = gen;
        _grounding = grounding;
        _seo = seo;
        _jobs = jobs;
        _user = user;
        _crawlerRepo = crawlerRepo;
        _rag = rag;
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
        if (create.ProjectSiteRunId is Guid crawlId && crawlId != Guid.Empty)
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
            projectSiteRunId = create.ProjectSiteRunId,
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
            if (request.ProjectSiteRunId is Guid aid && aid != Guid.Empty
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
            request.ProjectSiteRunId,
            sectionJson,
            Department: string.IsNullOrWhiteSpace(request.Department) ? "marketing" : request.Department.Trim(),
            ProjectId: request.ProjectId is Guid pid && pid != Guid.Empty ? pid : null), ct);

        return CreatedAtAction(nameof(GetCreate), new { id = created.Id }, created);
    }

    // Clients live here, on this controller, because it already owns this route prefix. A second
    // controller routed at api/geek-content-creator/clients was tried and produced an ambiguous
    // match: two actions claiming one path, which ASP.NET answers with a 500 before any
    // authentication runs. One path, one owner.
    //
    // They hold contact and billing details, so each action requires content-creator.manage. The
    // authority is shared across Geek apps; a valid token alone says nothing about being granted
    // this client's record.

    [HttpGet("clients/{id:guid}")]
    [Authorize(Policy = ContentCreatorAuthConstants.ManagePolicy)]
    public async Task<ActionResult<GccClientDto>> GetClient(Guid id, CancellationToken ct)
    {
        var client = await _repo.GetClientByIdAsync(id, ct);
        if (client is null) return NotFound();
        return Ok(client);
    }

    /// <summary>
    /// Every client, or the one with this name.
    /// </summary>
    /// <remarks>
    /// One route doing both because the path is one path. Without a name it is the list the
    /// operator picks from; with one it is the lookup the create flow has always used, answering
    /// 404 when there is no such client. Splitting them would mean two actions on
    /// "clients" again, which is the ambiguity this consolidation removes.
    /// </remarks>
    [HttpGet("clients")]
    [Authorize(Policy = ContentCreatorAuthConstants.ManagePolicy)]
    public async Task<ActionResult> GetClients([FromQuery] string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Ok(await _repo.ListClientsAsync(ct));

        var client = await _repo.GetClientByNameAsync(name, ct);
        if (client is null) return NotFound();
        return Ok(client);
    }

    [HttpPost("clients")]
    [Authorize(Policy = ContentCreatorAuthConstants.ManagePolicy)]
    public async Task<ActionResult<GccClientDto>> CreateClient([FromBody] CreateGccClientCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var invalid = ValidateClient(
            command.Name,
            command.ContactName,
            command.ContactEmail,
            command.BillingEmail,
            command.PaymentTermsDays,
            command.Currency,
            command.Rate);
        if (invalid is not null) return BadRequest(invalid);

        var client = await _repo.CreateClientAsync(command, ct);
        return CreatedAtAction(nameof(GetClient), new { id = client.Id }, client);
    }

    [HttpPut("clients/{id:guid}")]
    [Authorize(Policy = ContentCreatorAuthConstants.ManagePolicy)]
    public async Task<ActionResult<GccClientDto>> UpdateClient(
        Guid id,
        [FromBody] UpdateGccClientCommand command,
        CancellationToken ct)
    {
        if (command is null) return BadRequest("Command is required");
        if (id != command.Id) return BadRequest("The id in the route and the body must match.");

        var invalid = ValidateClient(
            command.Name,
            command.ContactName,
            command.ContactEmail,
            command.BillingEmail,
            command.PaymentTermsDays,
            command.Currency,
            command.Rate);
        if (invalid is not null) return BadRequest(invalid);

        return Ok(await _repo.UpdateClientAsync(command, ct));
    }

    /// <summary>
    /// Delete a client.
    /// </summary>
    /// <remarks>
    /// A client with projects is refused by gcc_projects.client_id, which is RESTRICT, and nothing
    /// here tries to talk it round. The only way to make the delete succeed would be to destroy
    /// the client's projects — their schedule, their log, and in time their hours — which is worse
    /// than a refused button.
    /// </remarks>
    [HttpDelete("clients/{id:guid}")]
    [Authorize(Policy = ContentCreatorAuthConstants.ManagePolicy)]
    public async Task<IActionResult> DeleteClient(Guid id, CancellationToken ct)
    {
        var deleted = await _repo.DeleteClientAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// The rules the database also enforces, said in a sentence.
    /// </summary>
    /// <remarks>
    /// The CHECK constraints on gcc_clients are the authority. This exists so a caller gets
    /// "currency must be a three-letter ISO-4217 code" rather than a constraint-violation stack
    /// trace; nothing passes here that the database would refuse.
    /// </remarks>
    private static string? ValidateClient(
        string? name,
        string? contactName,
        string? contactEmail,
        string? billingEmail,
        int paymentTermsDays,
        string? currency,
        decimal? rate)
    {
        if (string.IsNullOrWhiteSpace(name)) return "name is required.";
        if (string.IsNullOrWhiteSpace(contactName)) return "contactName is required.";
        if (string.IsNullOrWhiteSpace(contactEmail)) return "contactEmail is required.";
        if (string.IsNullOrWhiteSpace(billingEmail))
            return "billingEmail is required — it is stored, never derived from the contact email.";
        if (paymentTermsDays < 0) return "paymentTermsDays cannot be negative.";
        if (string.IsNullOrWhiteSpace(currency)) return "currency is required.";

        var trimmed = currency.Trim();
        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetter))
            return "currency must be a three-letter ISO-4217 code.";

        if (rate is <= 0)
            return "rate must be greater than zero, or omitted — a rate of zero is an unfilled field.";

        return null;
    }

    [HttpPost("creates/{id:guid}/generate")]
    public async Task<IActionResult> Generate(Guid id, [FromBody] ProviderRequest? request, CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();

        var section = GccGenerateService.ParseSiteSection(create.SiteSectionJson);
        try
        {
            GccGenerateService.ValidateSiteSectionGate(create.ProjectSiteRunId, section);
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
            || ex.Message.Contains("Site Analyzer", StringComparison.OrdinalIgnoreCase)
            // A grounding refusal is the operator's answer, not a server fault: the content type
            // declared evidence it must cite and that evidence is not available.
            || ex.Message.StartsWith("Refused:", StringComparison.OrdinalIgnoreCase))
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
        if (create.ProjectSiteRunId is not Guid profileId || profileId == Guid.Empty)
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
        if (create.ProjectSiteRunId is not Guid profileId || profileId == Guid.Empty)
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
            projectSiteRunId = profileId,
        };
    }

    private string? GetBearerToken()
    {
        var auth = Request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? auth["Bearer ".Length..].Trim()
            : null;
    }

    /// <summary>
    /// Folds retrieved, citable passages into the create's research so the existing quoteable
    /// prompt block (<c>GccGenerateService.cs:169</c>) carries them. Operator-uploaded quoteables
    /// are kept and retrieved ones appended; neither silently replaces the other.
    /// </summary>
    private static GccCreateDto MergeRetrievedEvidence(GccCreateDto create, GccGroundingOutcome grounding)
    {
        if (grounding.Pages.Count == 0)
        {
            return create;
        }

        var existing = GccResearchFetchService.Deserialize(create.ResearchJson);
        var quoteables = existing?.Quoteables.ToList() ?? [];
        var seen = new HashSet<string>(quoteables.Select(q => q.Url), StringComparer.OrdinalIgnoreCase);

        foreach (var page in grounding.Pages)
        {
            if (seen.Add(page.Url))
            {
                quoteables.Add(page);
            }
        }

        var merged = existing is null
            ? new GccResearchDocument(null, quoteables)
            : existing with { Quoteables = quoteables };

        return create with { ResearchJson = GccResearchFetchService.Serialize(merged) };
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

        // Grounding gate. Every grounding block downstream is conditional, so absent evidence used
        // to drop out silently and generation continued — a draft that reads identically whether
        // it was grounded or not. Required evidence is resolved here and its absence refuses.
        var grounding = await _grounding.ResolveAsync(create, contentType, ct);
        if (grounding.Refused)
            throw new InvalidOperationException($"Refused: {grounding.Refusal}");
        create = MergeRetrievedEvidence(create, grounding);

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

    /// <summary>
    /// Whether a project site has crawl evidence a create can use — and, when it does, the Run ID
    /// that holds it.
    ///
    /// Create is handed a Run ID, never a URL: the URL names a site, only a resolved run names the
    /// crawl that was committed and indexed for it. This is the gate into the workflow.
    ///
    /// Deliberately not the host index check on /api/rag/hosts-indexed. That asks the vector store
    /// whether a host has anything at all, which a crawl that fetched nothing can still satisfy.
    /// This runs the same retrieval PLAN runs, so presence is not mistaken for fitness.
    ///
    /// The resolver lives under ContentCreatorV2/ and is called in-process. That is a shared engine,
    /// not a v2 dependency — the surface this is served on is v1, and forking the resolver to make
    /// it "v1" would duplicate the retrieval probe and guarantee the two drift apart.
    /// </summary>
    [HttpPost("project-site/readiness")]
    public async Task<IActionResult> ProjectSiteReadiness(
        [FromBody] ProjectSiteReadinessRequest? request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var projectUrl = request?.ProjectUrl?.Trim();
        if (string.IsNullOrWhiteSpace(projectUrl))
            return BadRequest(new { error = "projectUrl required" });

        // Two questions, answered in order, because they fail differently.
        //
        // First: does the index hold this host, and under which run? An empty result means the index
        // could not be reached at all — not the same answer as "not indexed", and it must not read
        // as one.
        var indexed = await _rag.HostsIndexedAsync([projectUrl], ct).ConfigureAwait(false);
        if (indexed.Count == 0)
            return BadRequest(new { error = "The project URL could not be evaluated." });

        var row = indexed[0];
        if (!row.Indexed || string.IsNullOrWhiteSpace(row.RunId))
        {
            return Ok(new
            {
                seed = projectUrl,
                ready = false,
                runId = (string?)null,
                indexState = "missing",
                reason = "No crawl evidence is indexed for this host.",
            });
        }

        // Second: is that run actually finished and owned by this user? An indexed host whose run is
        // still crawling is not evidence a create can ground on.
        if (!Guid.TryParse(row.RunId, out var runId))
        {
            return Ok(new
            {
                seed = projectUrl,
                ready = false,
                runId = (string?)null,
                indexState = "unusable",
                reason = "The index named a run id that cannot be read.",
            });
        }

        var run = await _crawlerRepo.GetRunAsync(runId, ct).ConfigureAwait(false);
        if (run is null
            || !string.Equals(run.OwnerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                seed = projectUrl,
                ready = false,
                runId = (string?)null,
                indexState = "missing",
                reason = "The indexed run is not available to this user.",
            });
        }

        var complete = string.Equals(run.Status, "complete", StringComparison.OrdinalIgnoreCase);
        return Ok(new
        {
            seed = projectUrl,
            ready = complete,
            runId = complete ? run.Id.ToString("D") : null,
            indexState = run.RagState ?? run.Status,
            reason = complete ? null : $"The crawl for this host is {run.Status}, not complete.",
        });
    }

    /// <summary>
    /// Sections of the crawled project site whose heading matches the target keyword, ranked.
    ///
    /// Replaces the retired site-analyzer/profiles/{id}/hierarchy-match, which 404s: Site Analyzer is
    /// Geek-SEO's and was removed from this path. The hierarchy is derived from the crawl
    /// Geek-Crawler already performed — nothing here crawls.
    ///
    /// Returns a bare array, ranked best-first, because the caller shows every match rather than
    /// auto-selecting one: the same heading on several pages is a crawl finding the operator needs
    /// to see, not a tie to break silently.
    /// </summary>
    [HttpGet("project-site/runs/{runId:guid}/hierarchy-match")]
    public async Task<IActionResult> ProjectSiteHierarchyMatch(
        Guid runId,
        [FromQuery] string? keyword,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var target = keyword?.Trim();
        if (string.IsNullOrWhiteSpace(target))
            return BadRequest(new { error = "keyword required" });

        var run = await _crawlerRepo.GetRunAsync(runId, ct).ConfigureAwait(false);
        if (run is null) return NotFound();
        if (!string.Equals(run.OwnerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            return NotFound();

        // Blocks only — never Html, for the same reason the site-structure read takes this path.
        var pages = new List<GeekCrawlerPageDto>();
        var offset = 0;
        const int batch = 50;
        while (true)
        {
            var chunk = await _crawlerRepo.ListPageBlocksAsync(runId, batch, offset, ct).ConfigureAwait(false);
            if (chunk.Count == 0) break;
            pages.AddRange(chunk);
            if (chunk.Count < batch) break;
            offset += chunk.Count;
        }

        var structure = GeekCrawlerSiteStructure.Build(runId, pages);
        var matches = GccSiteStructureMatch.MatchAll(structure, [target]);

        return Ok(matches.Select(m => new
        {
            matchedHeading = m.MatchedHeading,
            path = m.Path,
            // The caller's kind union has no near-exact; it falls back to contains-heading, so map it
            // here rather than leaving the wire value to be silently reinterpreted.
            kind = m.Kind == "exact-heading" ? "exact-heading" : "contains-heading",
            childHeadings = m.ChildHeadings,
            sourcePageUrl = m.SourcePageUrl,
            toolsByHeading = m.RecommendedTools.Count == 0
                ? Array.Empty<object>()
                : [new { heading = m.MatchedHeading, tools = m.RecommendedTools.Select(tool => new { name = tool.Name, href = tool.Href }) }],
        }));
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
                        // reportProgress was added to this signature after these endpoints were
                        // retired, so ct is named rather than positional.
                        await _orchestrator.GenerateToolPagesAsync(
                            projectId,
                            notes,
                            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.Slug },
                            cancellationToken: ct),
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

    public sealed record CreateCreateRequest(
        Guid ClientId,
        string StartingContentType,
        string Topic,
        string? Notes,
        Guid? ProjectSiteRunId,
        SiteSectionContextDto? SiteSection,
        string? Department = null,
        /// <summary>The project this create belongs to. The project owns the partner and
        /// competitor URLs that grounding is resolved from; without it, content types requiring
        /// partner or competitor evidence are refused rather than generated ungrounded.</summary>
        Guid? ProjectId = null);

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
    public sealed record ProjectSiteReadinessRequest(string? ProjectUrl);

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
    public sealed record ContentApprovalRequest(bool Approved = true);
}
