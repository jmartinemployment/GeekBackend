using System.Text;
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
    private readonly GccGenerationCoordinator _coordinator;
    private readonly GccGenerateJobRunner _generateRunner;
    private readonly GccArtifactExportService _export;
    private readonly GccJobStore _jobs;
    private readonly ICurrentUserContext _user;
    private readonly HttpGeekCrawlerRepository _crawlerRepo;
    private readonly IGeekCrawlerRagClient _rag;
    private readonly GccAngleQuoteProbe _angleQuote;
    private readonly GccProjectSiteStructureReader _siteStructure;
    private readonly ILogger<GccController> _logger;

    public GccController(
        HttpGccRepository repo,
        IProjectStore projects,
        IContentGenerationOrchestrator orchestrator,
        IOptions<CompanyProfileOptions> company,
        GccGenerateService gen,
        GccGroundingResolver grounding,
        GccGenerationCoordinator coordinator,
        GccGenerateJobRunner generateRunner,
        GccArtifactExportService export,
        GccJobStore jobs,
        ICurrentUserContext user,
        HttpGeekCrawlerRepository crawlerRepo,
        IGeekCrawlerRagClient rag,
        GccAngleQuoteProbe angleQuote,
        GccProjectSiteStructureReader siteStructure,
        ILogger<GccController> logger)
    {
        _repo = repo;
        _projects = projects;
        _orchestrator = orchestrator;
        _company = company.Value;
        _gen = gen;
        _grounding = grounding;
        _coordinator = coordinator;
        _generateRunner = generateRunner;
        _export = export;
        _jobs = jobs;
        _user = user;
        _crawlerRepo = crawlerRepo;
        _rag = rag;
        _angleQuote = angleQuote;
        _siteStructure = siteStructure;
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

        // Always null/false, and that is what they already were. These asked Site Analyzer, which no
        // longer exists, with a crawl run id where it wanted a profile id -- see
        // TryBuildStaleGroundingResponseAsync. The fields stay on the wire because the client reads
        // them (content-creator-v2 gcc-api.ts:164-166, CreateDraftWorkspace.tsx:594-596); removing
        // them would be a contract change dressed up as a cleanup, and the UI they feed has been
        // unreachable for as long as the gate has been.
        DateTime? lastAnalyzedAtUtc = null;
        int? analysisAgeDays = null;
        const bool analysisStale = false;

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
        if (request.BriefJson is null && request.ResearchJson is null && request.Topic is null)
            return BadRequest("briefJson, researchJson and/or topic required");

        var existing = await _repo.GetCreateAsync(id, ct);
        if (existing is null) return NotFound();

        try
        {
            var updated = await _repo.UpdateBriefResearchAsync(
                id,
                new UpdateGccCreateBriefResearchCommand(
                    request.BriefJson, request.ResearchJson, request.Topic),
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

        var sourceId = Guid.NewGuid().ToString("N");
        var existing = GccResearchFetchService.Deserialize(create.ResearchJson)
            ?? new GccResearchDocument(null, []);

        // One path, no dispatch. A saved Google SERP is the only upload this endpoint takes:
        // PeopleAlsoAsk and the Wiki/.edu/.gov article path were removed on 2026-09-29 because the
        // UI no longer offers either (Jeff), and both left worse than dead code behind them. The
        // article path was the only producer of a quoteable with a non-http URL, which is what made
        // BuildResearchBlock label evidence by origin -- and that condition mislabelled every
        // partner page fetched by GccPartnerUrlResearchService as "operator-supplied", telling the
        // model its real evidence was untrusted prose. Only the standalone PeopleAlsoAsk *upload
        // category* went: SERP ingest still parses People-Also-Ask out of the saved SERP and the
        // panel still persists the operator's selection into brief.paaQuestions, so the
        // `paa:<question>` licensing channel and the FAQ section it gates are both live.
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

        // Only the SERP page and its source row. Quoteables are not filtered here any more: this
        // matched them on an uploaded-file URL prefix, and the path that produced such a URL was
        // deleted 2026-09-29 along with the UI control that fed it. Quoteables now come from the
        // crawl index and from fetched partner pages, neither of which this endpoint owns -- a
        // retrieved passage is removed by re-grounding the create, not by deleting an upload.
        var serpPages = (doc.SerpPages ?? []).Where(p => p.Id != sourceId).ToList();
        var sources = (doc.Sources ?? []).Where(s => s.Id != sourceId).ToList();
        var json = GccResearchFetchService.Serialize(
            doc with { SerpPages = serpPages, Sources = sources });
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

        // The refusals that cost nothing to decide stay here, so a mistyped request is still a
        // fast 400 rather than a job the operator has to watch fail. Same method the coordinator
        // itself calls -- one definition, not a controller copy that can drift from the real gate.
        var requested = GccGenerationCoordinator.NormalizeRequestedTypes(request?.OutputTypes);
        var refusal = GccGenerationCoordinator.ValidateRequestedTypes(requested);
        if (refusal is not null) return BadRequest(refusal);

        // Generation runs as a job and reports over SignalR rather than holding this request open.
        // One Tool page is partner extraction across every retrieved page plus a multi-call write,
        // and every selected content type runs its own complete pipeline -- longer than any
        // gateway between the browser and here will wait. Railway's edge was cutting the request
        // off with "upstream error" before the catch blocks below could report anything at all.
        //
        // Join the job on /hubs/workflow-realtime (JoinGccGenerate) for per-type events, or read
        // GET jobs/{id} on a cold load. See plans/generate-async-signalr.md.
        var job = _generateRunner.Start(
            create, section, provider, requested, mustMentionBlock, _user.UserId.ToString());

        return Accepted(new { jobId = job.Id, createId = create.Id, status = job.Status });
    }

    /// <summary>
    /// A create's generated content as a zip: one file per artifact foldered by content type, and
    /// the image prompts in their own parallel tree rather than mixed into the prose.
    /// </summary>
    /// <remarks>
    /// The two export services that already existed both read stores this path never writes -- v1's
    /// reads GeneratedContent rows on a Workflow project, GccV2's reads GccV2 jobs -- so exporting
    /// a create through either returned an empty archive.
    /// </remarks>
    [HttpGet("creates/{id:guid}/export/html")]
    public async Task<IActionResult> ExportCreateHtml(Guid id, CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();

        var documents = await _export.ExportAsync(id, ct);
        if (documents.Count == 0)
            return BadRequest("Nothing to export: this create has no generated artifacts yet.");

        using var zipStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
            zipStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var document in documents)
            {
                var entry = archive.CreateEntry(document.FileName, System.IO.Compression.CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                if (document.BinaryContent is { Length: > 0 } bytes)
                {
                    await entryStream.WriteAsync(bytes, ct);
                }
                else
                {
                    await using var writer = new StreamWriter(entryStream);
                    await writer.WriteAsync(document.Content ?? string.Empty);
                }
            }
        }

        zipStream.Position = 0;
        return File(zipStream.ToArray(), "application/zip", $"{id}-content-export.zip");
    }
    /// <summary>
    /// Looks up this create's real "must mention" sub-topics from its analyzed site's persisted
    /// page-section trees (see GccGenerateService.BuildMustMentionSubtopicsBlock). Returns null
    /// (no injection, no failure) when there's no attached analysis, no bearer token, or no
    /// deterministic slug match — a missing/uncertain match must never block Generate or inject
    /// a guessed subtree.
    /// </summary>
    /// <summary>
    /// The operator's own site structure around this create's topic -- the headings their site
    /// already carries under it, so the piece covers what the site says it covers and can point at
    /// pages that exist.
    /// </summary>
    /// <remarks>
    /// This read the retired Site Analyzer until 2026-09-23, and passed it the wrong kind of id:
    /// create.ProjectSiteRunId is a Geek-Crawler-v2 run id and GetPageSectionTreesAsync wants a
    /// Site Analyzer profile id -- the local was even named profileId. Site Analyzer's routes were
    /// deleted (582a171, 5072820), so the call could only fail, and it failed to null. Generation
    /// then proceeded with no site structure at all, on every create, silently.
    ///
    /// The bearer check above it was a second silent null, and a worse one now that generate runs
    /// as a background job where no bearer is captured.
    ///
    /// This is the same read the live project-site/runs/{runId}/hierarchy-match route does -- the
    /// one the operator confirmed working before the panel was hidden. Blocks, never Html.
    /// </remarks>
    private async Task<string?> TryBuildMustMentionBlockAsync(GccCreateDto create, CancellationToken ct)
    {
        if (create.ProjectSiteRunId is not Guid runId || runId == Guid.Empty)
            return null;
        if (string.IsNullOrWhiteSpace(create.Topic))
            return null;

        var structure = await _siteStructure.ReadAsync(runId, ct).ConfigureAwait(false);
        if (structure is null)
        {
            _logger.LogInformation(
                "Site structure: run {RunId} returned no pages, so this create generates without it.", runId);
            return null;
        }

        var matches = GccSiteStructureMatch.MatchAll(structure, [create.Topic.Trim()]);
        var matched = matches.FirstOrDefault(m => m.ChildHeadings.Length > 0) ?? matches.FirstOrDefault();
        if (matched is null)
        {
            _logger.LogInformation(
                "Site structure: nothing on the site matches \"{Topic}\", so this create generates without it.",
                create.Topic);
            return null;
        }

        // Formatted by GccMustMention so the guard can read back the subtopics it names -- they
        // are compulsory, and a heading covering one needs a source to be licensed against.
        return GccMustMention.Format(matched.MatchedHeading, matched.SourcePageUrl, matched.ChildHeadings);
    }

    /// <summary>
    /// Returns null, always: Generate has no staleness gate, and has never had one that could fire.
    /// </summary>
    /// <remarks>
    /// A thirty-day threshold constant sat above this, documented as "requires an explicit operator
    /// choice before Generate proceeds". Nothing read it. It is removed, because a declared policy
    /// that no code enforces is read as enforced — the same defect as documenting a boundary as
    /// fail-closed while it accepts the bad input.
    /// </remarks>
    private async Task<object?> TryBuildStaleGroundingResponseAsync(
        GccCreateDto create,
        bool acknowledged,
        CancellationToken ct)
    {
        // There is no staleness check on this path, and there has not been one that could fire.
        //
        // This asked Site Analyzer, a service that no longer exists (Jeff, 2026-10-01), over a route
        // the repo already records as deleted (:1301-1302, :604-608) -- and asked it with the wrong
        // kind of id: create.ProjectSiteRunId is a Geek-Crawler-v2 run id (Entities.cs:32-37) and
        // the parameter it was bound to was literally named profileId. The sibling method at
        // :604-608 records that exact defect being found and fixed next door; this one was missed.
        // Every failure mode returned null, which Generate reads as "proceed", so the gate was a
        // doomed HTTP call on every Generate whose answer was discarded.
        //
        // Returning null unconditionally is therefore not a behaviour change: it is what every path
        // through the old body did. The method stays rather than being inlined away, because
        // "should evidence age gate generation" is a real question and this is where its answer
        // belongs. The data to answer it exists on the crawl run -- ContentReadyAt and
        // RagIndexedAtUtc -- and owes nothing to Site Analyzer.
        await Task.CompletedTask;
        return null;
    }

    private string? GetBearerToken()
    {
        var auth = Request.Headers.Authorization.ToString();
        return auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? auth["Bearer ".Length..].Trim()
            : null;
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

        // Revise wrote every type with the blog prompt, so a pillar or a tool page was rewritten to
        // blog length -- 2,000 words against their 3,500 -- and lost a third of itself on the first
        // press. The artifact knows what it is.
        var artifact = await _repo.GetArtifactAsync(current.ArtifactId, ct);

        try
        {
            var revised = await _gen.ReviseAsync(
                current.BodyDocumentJson,
                request.Feedback,
                request.Scope ?? "full",
                request.SectionPath,
                provider,
                ct,
                artifact?.Type);
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

        // The artifact's own type, not the caller's word for it and not a default. Length and section
        // thresholds are per type (GccV2LongFormTypes.GetSeoLengthRules), and this route used to omit
        // it -- so a 3,000-word pillar and a 3,000-word tool page were both graded against the blog's
        // 1,800 and reported as passing on length.
        var artifact = await _repo.GetArtifactAsync(version.ArtifactId, ct);
        if (artifact is null) return NotFound();

        // A topic is context plus keyword -- "Accounts Payable: Automated Data Entry & Processing"
        // -- and the caller sends the whole thing. Scoring the whole thing asks whether the lede
        // contains both halves verbatim, which no readable sentence does, so keyword-in-lede and
        // keyword-in-heading failed on drafts that used the keyword correctly throughout.
        var report = GccGenerateService.AnalyzeSeo(
            version.BodyDocumentJson, GccTargetKeyword.FromTopic(keyword), artifact.Type);
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

    // Disabled 2026-09-22 (Jeff): Repurpose never called ValidateSiteSectionGate, so every type it
    // produces -- including aiTool -- generated without the Project Site crawl requirement
    // creates/{id}/generate enforces unconditionally for everything else. RepurposeInternal is kept
    // as a real, complete implementation rather than deleted so it can be re-enabled once it carries
    // the same gate; only the route itself is disabled.
    [HttpPost("versions/{id:guid}/repurpose")]
    public Task<ActionResult<object>> Repurpose(Guid id, [FromBody] MixRequest? request, CancellationToken ct) =>
        Task.FromResult<ActionResult<object>>(StatusCode(403,
            "Repurpose is disabled -- it does not enforce the Project Site grounding gate that "
            + "creates/{id}/generate requires for every content type. Use Generate instead."));

    private async Task<ActionResult<object>> RepurposeInternal(Guid id, MixRequest? request, CancellationToken ct)
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
        var create = await _repo.GetCreateAsync(createId, ct);
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
                        ct,
                        create);
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
    /// Whether each declared partner can answer the question this brief's Angle demands of the
    /// block quotation — asked here, before Generate, rather than discovered after a paid draft.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project form already validates these URLs, but it asks a volume question — indexed, with
    /// enough pages and chunks — and volume is not fitness. A partner can carry nine thousand chunks
    /// and still say nothing that answers <c>problem_solution</c> for this keyword. The Angle lives
    /// on the brief, so this is the first point at which the real question exists to be asked.
    /// </para>
    /// <para>
    /// Same shape as <c>project-site/readiness</c> above, and for the same stated reason: it runs
    /// the retrieval and the selection that generation will run, so presence is not mistaken for
    /// fitness and a partner that passes here cannot fail generation for want of a quote.
    /// </para>
    /// </remarks>
    [HttpPost("brief/partner-quote-readiness")]
    public async Task<IActionResult> PartnerQuoteReadiness(
        [FromBody] PartnerQuoteReadinessRequest? request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var topic = (request?.Topic ?? string.Empty).Trim();
        if (topic.Length == 0)
            return BadRequest(new { error = "topic is required — it is the subject of the question." });
        if (request?.ProjectId is not { } projectId || projectId == Guid.Empty)
            return BadRequest(new { error = "projectId is required — it owns the declared partners." });

        // Read the partners from the project rather than taking them from the caller. The project's
        // declared list is the one the content is obliged to name, and it is not the same set as the
        // hosts that happen to be indexed as `partner` — a URL can be indexed under one crawl type
        // and declared under another.
        var project = await _repo.GetProjectAsync(projectId, ct);
        if (project is null) return NotFound();

        var urls = (project.PartnerUrls ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (urls.Count == 0)
        {
            return BadRequest(new
            {
                error = "This project declares no partner URLs, so there is nothing to quote.",
            });
        }

        // No angle, no question. Defaulting to one would validate every partner against a question
        // the brief never asked, which is worse than refusing to answer.
        var spec = GccAngleQuoteQuestion.For(request?.Angle, topic);
        if (spec is null)
        {
            return BadRequest(new
            {
                error = "This brief has no recognised Angle for SEO, so there is no question for "
                    + "the block quotation to answer. Set the angle, then check again.",
            });
        }

        // The index is the one place that resolves a URL to the crawl behind it, and it is the same
        // answer the project form reads. Asked once for all partners rather than once per probe.
        var rows = await _rag.HostsIndexedAsync(urls, ct);
        if (rows.Count == 0)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { error = "The index could not be reached, so no partner could be checked." });
        }

        var runByUrl = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.Indexed && Guid.TryParse(row.RunId, out var parsed))
                runByUrl[row.Url] = parsed;
        }

        // Concurrently, for the same reason partner extraction runs its pages concurrently: each
        // probe is an independent round trip, and ConcurrencyLimitingContentGenerationProvider
        // already caps in-flight provider calls globally, so this rides that limit rather than
        // introducing a second one. Results are read back in request order.
        var findings = await Task.WhenAll(urls.Select(url =>
            _angleQuote.ProbeAsync(
                spec,
                url,
                runByUrl.TryGetValue(url, out var runId) ? runId : Guid.Empty,
                ct)));

        return Ok(new
        {
            angle = spec.Angle,
            question = spec.Rule,
            canAnswer = findings.Count(f => f.CanAnswer),
            declared = urls.Count,
            results = findings.Select(f => new
            {
                url = f.PartnerUrl,
                canAnswer = f.CanAnswer,
                outcome = f.Outcome.ToString().ToLowerInvariant(),
                quote = f.QuoteText,
                cite = f.CiteUrl,
                reason = f.Reason,
            }),
        });
    }

    /// <param name="ProjectId">Owns the declared partner URLs the question is asked of.</param>
    /// <param name="Topic">The target keyword — the subject the question is about.</param>
    /// <param name="Angle">The brief's Angle for SEO, which decides which question is asked.</param>
    public sealed record PartnerQuoteReadinessRequest(
        Guid? ProjectId,
        string? Topic,
        string? Angle);
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


    private static bool TryParseProvider(string? raw, out ContentGeneratorProvider provider, out string? error)
    {
        provider = ContentGeneratorProvider.OpenAi;
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (Enum.TryParse(raw, ignoreCase: true, out provider)) return true;
        error = $"Unknown provider '{raw}'. Valid: {string.Join(", ", Enum.GetNames<ContentGeneratorProvider>())}.";
        return false;
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
    public sealed record UpdateBriefResearchRequest(
        string? BriefJson,
        string? ResearchJson,
        /// <summary>The corrected topic, or null to leave it. See the command's own remarks.</summary>
        string? Topic = null);
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
