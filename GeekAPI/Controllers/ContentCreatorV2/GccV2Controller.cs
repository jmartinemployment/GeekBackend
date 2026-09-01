using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.ContentCreatorV2.GeekCrawler;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekAPI.Services.ContentCreatorV2.Plan;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>
/// Content Creator v2 API. Parallel to v1 <c>api/geek-content-creator</c>. Jobs are event-driven —
/// generate/approve/cancel persist then wake the in-process worker; there is no client poll route.
/// </summary>
[ApiController]
[Route("api/geek-content-creator-v2")]
public class GccV2Controller : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ICurrentUserContext _user;
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2JobWake _wake;
    private readonly GccV2JobEventWriter _events;
    private readonly GccV2BrandKitBuilder _brandKitBuilder;
    private readonly GccV2SiteHierarchyService _siteHierarchy;
    private readonly GccV2GeekCrawlerResearchResolver _researchResolver;
    private readonly ILogger<GccV2Controller> _logger;

    public GccV2Controller(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2JobWake wake,
        GccV2JobEventWriter events,
        GccV2BrandKitBuilder brandKitBuilder,
        GccV2SiteHierarchyService siteHierarchy,
        GccV2GeekCrawlerResearchResolver researchResolver,
        ILogger<GccV2Controller> logger)
    {
        _user = user;
        _repo = repo;
        _wake = wake;
        _events = events;
        _brandKitBuilder = brandKitBuilder;
        _siteHierarchy = siteHierarchy;
        _researchResolver = researchResolver;
        _logger = logger;
    }

    [HttpGet("health")]
    public ActionResult<object> Health() =>
        Ok(new
        {
            ok = true,
            product = "geek-content-creator-v2",
            userId = _user.IsAuthenticated ? _user.UserId.ToString("D") : null,
        });

    [HttpPost("creates")]
    public async Task<ActionResult<GccV2CreateDto>> CreateCreate([FromBody] CreateCreateRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "title is required" });

        SiteSectionContextDto? section = null;
        string? siteSectionJson = null;
        if (request.SiteSection is { } sectionElement && sectionElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            siteSectionJson = sectionElement.GetRawText();
            try
            {
                section = GccV2SiteSection.ParseSiteSection(siteSectionJson);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Invalid siteSection: {ex.Message}" });
            }
        }

        if (section is null || section.RelatedPages is null || section.RelatedPages.Count == 0)
            return BadRequest(new { error = "siteSection with non-empty relatedPages is required — complete a project-site crawl first." });

        var runId = section.ProjectSiteCrawlRunId;
        if (runId == Guid.Empty)
            return BadRequest(new { error = "siteSection.projectSiteCrawlRunId is required." });

        var siteUrl = string.IsNullOrWhiteSpace(request.SiteUrl)
            ? section.RelatedPages[0].Url
            : request.SiteUrl.Trim();

        var create = await _repo.CreateCreateAsync(
            new CreateGccV2CreateCommand(
                _user.UserId.ToString("D"),
                request.Title,
                request.ContentType,
                siteSectionJson,
                siteUrl,
                runId),
            ct);
        return Ok(create);
    }

    [HttpGet("creates")]
    public async Task<ActionResult<IReadOnlyList<object>>> ListCreates(CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var creates = await _repo.ListCreatesAsync(_user.UserId.ToString("D"), ct);
        var summaries = new List<object>();
        foreach (var create in creates)
        {
            var jobs = await _repo.ListJobsByCreateAsync(create.Id, ct);
            summaries.Add(new
            {
                create.Id,
                create.Title,
                create.ContentType,
                create.CreatedAtUtc,
                create.UpdatedAtUtc,
                jobContentTypes = jobs
                    .Select(j => j.ContentType)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            });
        }
        return Ok(summaries);
    }

    [HttpGet("creates/{id:guid}")]
    public async Task<ActionResult<GccV2CreateDto>> GetCreate(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null || !IsOwner(create.OwnerUserId)) return NotFound();
        return Ok(create);
    }

    /// <summary>All jobs for a create (oldest first) — multi-draft switcher on the create page.</summary>
    [HttpGet("creates/{id:guid}/jobs")]
    public async Task<ActionResult<IReadOnlyList<object>>> ListJobsForCreate(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null || !IsOwner(create.OwnerUserId)) return NotFound();
        var jobs = await _repo.ListJobsByCreateAsync(id, ct);
        var enriched = new List<object>();
        foreach (var job in jobs)
        {
            string? tabLabel = null;
            if (string.Equals(job.ContentType, "tool", StringComparison.OrdinalIgnoreCase))
            {
                var brief = await _repo.GetBriefAsync(job.BriefId, ct);
                tabLabel = GccV2ToolPageTargetParser.ResolveTabLabel(job.ContentType, brief?.RawBriefJson);
            }

            enriched.Add(new
            {
                job.Id,
                job.ContentType,
                job.BriefId,
                job.OwnerUserId,
                job.CreateId,
                job.Stage,
                job.Status,
                job.AttemptCount,
                job.ResultJson,
                job.Error,
                job.TokensUsed,
                job.CreatedAtUtc,
                job.UpdatedAtUtc,
                job.CompletedAtUtc,
                projectSiteCrawlRunId = job.ProjectSiteCrawlRunId ?? job.SiteAnalysisProfileId,
                tabLabel,
            });
        }

        return Ok(enriched);
    }

    /// <summary>Most recent job for a create — used when opening Canvas without <c>?jobId=</c> in the URL.</summary>
    [HttpGet("creates/{id:guid}/latest-job")]
    public async Task<ActionResult<GccV2JobDto>> GetLatestJobForCreate(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null || !IsOwner(create.OwnerUserId)) return NotFound();
        var job = await _repo.GetLatestJobByCreateAsync(id, ct);
        return job is null ? NotFound() : Ok(job);
    }

    /// <summary>
    /// Resolve crawl + operator partner tools for a create without starting WRITE jobs.
    /// Operator must review/confirm this list before <c>POST .../generate</c>.
    /// </summary>
    [HttpPost("creates/{id:guid}/partner-tools/preflight")]
    public async Task<ActionResult<object>> PreflightPartnerTools(
        Guid id,
        [FromBody] GenerateRequest? request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        if (request?.ProjectSiteCrawlRunId is not { } runId || runId == Guid.Empty)
            return BadRequest(new { error = "projectSiteCrawlRunId is required." });

        try
        {
            var section = GccV2SiteSection.ParseSiteSection(create.SiteSectionJson);
            GccV2SiteSection.ValidateSiteSectionGate(runId, section);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var rawBriefJson = request?.Brief is { } briefElement
            ? briefElement.GetRawText()
            : null;
        rawBriefJson = await TryMergeHierarchyPlanAsync(rawBriefJson, request, create.Title, ct);

        // Partner research HTML merge runs at generate — preflight only needs hierarchyPlan tool rows.
        var tools = GccV2PartnerUrlResearchService.CollectPartnerToolRows(rawBriefJson);
        string? matchedHeading = null;
        string? matchTopic = null;
        string[]? matchedPath = null;
        object? siteHierarchy = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBriefJson) ? "{}" : rawBriefJson);
            if (doc.RootElement.TryGetProperty("hierarchyPlan", out var plan)
                && plan.ValueKind == JsonValueKind.Object)
            {
                if (plan.TryGetProperty("matchedHeading", out var mh) && mh.ValueKind == JsonValueKind.String)
                    matchedHeading = mh.GetString();
                if (plan.TryGetProperty("matchTopic", out var mt) && mt.ValueKind == JsonValueKind.String)
                    matchTopic = mt.GetString();
                if (plan.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.Array)
                {
                    matchedPath = pathEl.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToArray();
                }
            }

            if (doc.RootElement.TryGetProperty("siteHierarchy", out var sh)
                && sh.ValueKind == JsonValueKind.Object)
            {
                siteHierarchy = JsonSerializer.Deserialize<JsonElement>(sh.GetRawText());
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        // #region agent log
        GeekAPI.Diagnostics.AgentDebugLog.Write(
            "E",
            "GccV2Controller.PreflightPartnerTools",
            "Partner tools preflight",
            new
            {
                createId = id,
                toolCount = tools.Count,
                matchedHeading,
                matchTopic,
                matchedPath,
                hasSiteHierarchy = siteHierarchy is not null,
                firstToolNames = tools.Take(8).Select(t => t.Name).ToList(),
                toolsPathCount = tools.Count(t =>
                    GccV2SiteSection.HrefLooksLikeOnSiteToolPage(t.Url)),
            });
        // #endregion

        var message = tools.Count > 0
            ? $"Found {tools.Count} partner tool(s). Each will be discussed in the draft. Confirm to start content creation."
            : "No partner tools found from the site hierarchy or pasted URLs. Confirm to continue without them, or add tool URLs and re-check.";

        const string externalResearchNote =
            "On-site tools come from your project crawl. External partners are pulled from stored crawl pages when available; if a partner crawl did not finish, that partner is skipped and generate still runs.";

        return Ok(new
        {
            createId = id,
            matchedHeading,
            matchTopic,
            path = matchedPath,
            toolCount = tools.Count,
            toolsFound = tools.Count > 0,
            tools,
            siteHierarchy,
            message,
            externalResearchNote,
        });
    }

    /// <summary>Creates a brief stub + one pending job per content type, wakes the worker(s).
    /// Requires <see cref="GenerateRequest.PartnerToolsConfirmed"/> after preflight.</summary>
    [HttpPost("creates/{id:guid}/generate")]
    public async Task<ActionResult<object>> Generate(Guid id, [FromBody] GenerateRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        if (request?.PartnerToolsConfirmed != true)
            return BadRequest(new
            {
                error = "Confirm partner tools before starting content creation (POST .../partner-tools/preflight, then generate with partnerToolsConfirmed=true).",
            });

        if (request?.ProjectSiteCrawlRunId is not { } runId || runId == Guid.Empty)
            return BadRequest(new { error = "projectSiteCrawlRunId is required — start from a project-site crawl." });

        SiteSectionContextDto? section;
        try
        {
            section = GccV2SiteSection.ParseSiteSection(create.SiteSectionJson);
            GccV2SiteSection.ValidateSiteSectionGate(runId, section);
            if (section is null || section.RelatedPages is null || section.RelatedPages.Count == 0)
                return BadRequest(new { error = "Create is missing siteSection with relatedPages — complete a project-site crawl first." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        GccV2BrandKitContent kit;
        try
        {
            kit = await _brandKitBuilder.BuildAsync(
                runId,
                create.SiteUrl,
                section,
                ct);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var contentTypes = ResolveContentTypes(request, create.ContentType);
        var primaryType = contentTypes[0];

        var rawBriefJson = request?.Brief is { } briefElement
            ? briefElement.GetRawText()
            : null;

        rawBriefJson = EnsureBriefContentTypes(rawBriefJson, contentTypes, primaryType);
        if (contentTypes.Any(t => string.Equals(t, "tool", StringComparison.OrdinalIgnoreCase)))
            rawBriefJson = GccV2ToolPageTargetParser.MergeOverviewTarget(rawBriefJson, request?.TargetKeyword);
        rawBriefJson = await TryMergeSiteHierarchyAsync(rawBriefJson, create.SiteUrl, ct);
        rawBriefJson = await TryMergeHierarchyPlanAsync(rawBriefJson, request, create.Title, ct);

        var mergeResult = await _researchResolver.MergeExternalResearchAsync(
            _user.UserId.ToString("D"),
            rawBriefJson,
            create.SiteUrl,
            runId,
            ct);
        rawBriefJson = mergeResult.BriefJson;

        var brief = await _repo.CreateBriefAsync(
            new CreateGccV2BriefCommand(id, request?.TargetKeyword, primaryType, RawBriefJson: rawBriefJson),
            ct);

        await _repo.CreateBrandKitAsync(
            new CreateGccV2BrandKitCommand(
                runId,
                ClientId: null,
                KitJson: JsonSerializer.Serialize(kit, JsonOpts),
                VoiceStatus: "provisional"),
            ct);

        var jobIds = new List<Guid>();
        foreach (var contentType in contentTypes)
        {
            var job = await _repo.CreateJobAsync(
                new CreateGccV2JobCommand(id, _user.UserId.ToString("D"), contentType, brief.Id, ProjectSiteCrawlRunId: runId),
                ct);
            await _events.AppendAsync(job.Id, _user.UserId, "JobQueued", new { jobId = job.Id, briefId = brief.Id, contentType }, ct: ct);
            _wake.Wake(job.Id);
            jobIds.Add(job.Id);
        }

        return Accepted(new
        {
            jobId = jobIds[0],
            jobIds,
            partnerResearchWarnings = mergeResult.PartnerResearchWarnings,
        });
    }

    /// <summary>
    /// Mobile homepage heading tree onto the brief as structured <c>siteHierarchy</c>. Soft-fail.
    /// Skips rebuild when <paramref name="rawBriefJson"/> already has siteHierarchy (early UI fetch).
    /// </summary>
    private async Task<string?> TryMergeSiteHierarchyAsync(
        string? rawBriefJson,
        string? siteUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(siteUrl))
            return rawBriefJson;

        if (BriefAlreadyHasSiteHierarchy(rawBriefJson))
            return rawBriefJson;

        try
        {
            var hierarchy = await _siteHierarchy.BuildHomepageAsync(siteUrl, ct);
            if (hierarchy is null)
                return rawBriefJson;

            _logger.LogInformation(
                "Attached mobile siteHierarchy for {Homepage} with {PageCount} page(s), {RootCount} root heading(s).",
                hierarchy.HomepageUrl,
                hierarchy.Pages.Count,
                hierarchy.Pages.Sum(p => p.Roots.Count));

            return GccV2SiteHierarchyService.MergeIntoBriefJson(rawBriefJson, hierarchy);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "siteHierarchy crawl soft-failed for siteUrl {SiteUrl}", siteUrl);
            return rawBriefJson;
        }
    }

    private static bool BriefAlreadyHasSiteHierarchy(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("siteHierarchy", out var sh)
                || sh.ValueKind != JsonValueKind.Object)
                return false;
            if (!sh.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array)
                return false;
            return pages.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Build mobile homepage hierarchy without starting WRITE / tool preflight.
    /// </summary>
    [HttpPost("site-hierarchy")]
    public async Task<ActionResult<object>> BuildSiteHierarchy(
        [FromBody] SiteHierarchyRequest? request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.SiteUrl))
            return BadRequest(new { error = "siteUrl is required." });

        var hierarchy = await _siteHierarchy.BuildHomepageAsync(request.SiteUrl, ct);
        if (hierarchy is null)
        {
            return Ok(new
            {
                siteHierarchy = (object?)null,
                message = "Mobile hierarchy crawl soft-failed (browser unavailable or fetch error).",
            });
        }

        return Ok(new { siteHierarchy = hierarchy });
    }

    public record SiteHierarchyRequest(string SiteUrl);

    /// <summary>
    /// Prefetch hierarchy match + recommended tools from CC mobile <c>siteHierarchy</c> only.
    /// Tight heading match + structured Links — no SA TreeJson / most-tools / markdown harvest.
    /// </summary>
    private Task<string?> TryMergeHierarchyPlanAsync(
        string? rawBriefJson,
        GenerateRequest? request,
        string? createTitle,
        CancellationToken ct)
    {
        _ = ct;
        var seeds = new List<string>();
        if (!string.IsNullOrWhiteSpace(request?.TargetKeyword))
            seeds.Add(request!.TargetKeyword!.Trim());
        if (!string.IsNullOrWhiteSpace(createTitle)
            && !seeds.Any(a => a.Equals(createTitle.Trim(), StringComparison.OrdinalIgnoreCase)))
            seeds.Add(createTitle.Trim());
        var titleFromBrief = TryReadBriefTitle(rawBriefJson);
        if (!string.IsNullOrWhiteSpace(titleFromBrief)
            && !seeds.Any(a => a.Equals(titleFromBrief, StringComparison.OrdinalIgnoreCase)))
            seeds.Add(titleFromBrief!);

        if (seeds.Count == 0) return Task.FromResult(rawBriefJson);

        try
        {
            var hierarchy = GccV2HierarchyToolMatch.TryParseSiteHierarchy(rawBriefJson);
            var match = GccV2HierarchyToolMatch.Match(hierarchy, seeds);
            if (match is null)
            {
                _logger.LogInformation(
                    "No tight siteHierarchy match for seeds [{Seeds}]; leaving hierarchyPlan unset.",
                    string.Join(", ", seeds));
                return Task.FromResult(rawBriefJson);
            }

            var plan = new
            {
                matchedHeading = match.MatchedHeading,
                sourcePageUrl = match.SourcePageUrl,
                path = match.Path,
                kind = match.Kind,
                childHeadings = match.ChildHeadings,
                recommendedTools = match.RecommendedTools
                    .Select(t => new { name = t.Name, href = t.Href })
                    .ToList(),
                matchTopic = match.MatchTopic,
            };

            _logger.LogInformation(
                "Hierarchy plan from siteHierarchy: matched '{Heading}' ({Kind}) with {ToolCount} tool(s) via topic '{Topic}'.",
                match.MatchedHeading,
                match.Kind,
                match.RecommendedTools.Count,
                match.MatchTopic);

            return Task.FromResult(MergeHierarchyPlanIntoBriefJson(rawBriefJson, plan));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "siteHierarchy tool match failed; PLAN will use content-type templates instead.");
            return Task.FromResult(rawBriefJson);
        }
    }

    private static string? TryReadBriefTitle(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            foreach (var key in new[] { "title", "Title" })
            {
                if (doc.RootElement.TryGetProperty(key, out var t)
                    && t.ValueKind == JsonValueKind.String)
                {
                    var s = t.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private string? ExtractBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return null;
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : header.Trim();
    }

    private static string? MergeHierarchyPlanIntoBriefJson(string? rawBriefJson, object hierarchyPlan)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBriefJson) ? "{}" : rawBriefJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return rawBriefJson;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "hierarchyPlan", StringComparison.OrdinalIgnoreCase)) continue;
                    prop.WriteTo(writer);
                }
                writer.WritePropertyName("hierarchyPlan");
                JsonSerializer.Serialize(writer, hierarchyPlan, JsonOpts);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return rawBriefJson;
        }
    }

    [HttpGet("jobs/{id:guid}")]
    public async Task<ActionResult<GccV2JobDto>> GetJob(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var job = await _repo.GetJobAsync(id, ct);
        if (job is null || !IsOwner(job.OwnerUserId)) return NotFound();
        return Ok(job);
    }

    [HttpGet("jobs/{id:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<GccV2JobEventDto>>> GetJobEvents(
        Guid id,
        [FromQuery] int afterSeq,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var job = await _repo.GetJobAsync(id, ct);
        if (job is null || !IsOwner(job.OwnerUserId)) return NotFound();

        var events = await _repo.GetJobEventsAsync(id, afterSeq, ct);
        return Ok(events);
    }

    [HttpPost("jobs/{id:guid}/approve-outline")]
    public async Task<ActionResult<GccV2JobDto>> ApproveOutline(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var job = await _repo.GetJobAsync(id, ct);
        if (job is null || !IsOwner(job.OwnerUserId)) return NotFound();
        if (!string.Equals(job.Status, "awaiting_outline_approval", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Job is '{job.Status}', not awaiting outline approval." });

        var updated = await _repo.PatchJobAsync(id, new PatchGccV2JobCommand(Stage: "write", Status: "pending"), ct);
        await _events.AppendAsync(id, _user.UserId, "OutlineApproved", new { jobId = id }, ct: ct);
        _wake.Wake(id);

        var siblings = GccV2OutlineApproval.SiblingJobsToAdvance(
            await _repo.ListJobsByCreateAsync(job.CreateId, ct),
            id);
        foreach (var sibling in siblings)
        {
            await _repo.PatchJobAsync(
                sibling.Id,
                new PatchGccV2JobCommand(Stage: "write", Status: "pending"),
                ct);
            await _events.AppendAsync(
                sibling.Id,
                _user.UserId,
                "OutlineApproved",
                new { jobId = sibling.Id, viaSibling = id },
                ct: ct);
            _wake.Wake(sibling.Id);
        }

        return Ok(updated);
    }

    [HttpPost("jobs/{id:guid}/accept-brandkit")]
    public async Task<ActionResult<GccV2JobDto>> AcceptBrandKit(
        Guid id,
        [FromBody] AcceptBrandKitRequest? request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var job = await _repo.GetJobAsync(id, ct);
        if (job is null || !IsOwner(job.OwnerUserId)) return NotFound();
        if (!string.Equals(job.Status, "awaiting_brandkit_approval", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Job is '{job.Status}', not awaiting brand kit approval." });
        if (ResolveCrawlRunId(job) is not { } crawlRunId)
            return BadRequest(new { error = "Job has no projectSiteCrawlRunId." });

        var kits = await _repo.ListBrandKitsByProfileAsync(crawlRunId, ct);
        var kit = kits.FirstOrDefault();
        if (kit is null)
            return BadRequest(new { error = "No brand kit found for this job's site profile." });

        var kitJson = MergeBrandKitEdits(kit.KitJson, request);
        await _repo.PatchBrandKitAsync(
            kit.Id,
            new PatchGccV2BrandKitCommand(
                KitJson: kitJson,
                VoiceStatus: "accepted",
                AcceptedAtUtc: DateTimeOffset.UtcNow),
            ct);

        await _events.AppendAsync(id, _user.UserId, "BrandKitAccepted", new { jobId = id, brandKitId = kit.Id }, ct: ct);

        var updated = await AdvanceAfterBrandKitAsync(job, id, kit.Id, ct);

        var siblings = await _repo.ListJobsByCreateAsync(job.CreateId, ct);
        foreach (var sibling in siblings)
        {
            if (sibling.Id == id) continue;
            if (!string.Equals(sibling.Status, "awaiting_brandkit_approval", StringComparison.OrdinalIgnoreCase))
                continue;
            await AdvanceAfterBrandKitAsync(sibling, id, kit.Id, ct);
        }

        return Ok(updated);
    }

    private async Task<GccV2JobDto> AdvanceAfterBrandKitAsync(
        GccV2JobDto job,
        Guid acceptedViaJobId,
        Guid brandKitId,
        CancellationToken ct)
    {
        var shortForm = IsShortFormContentType(job.ContentType);
        if (shortForm)
        {
            var updated = await _repo.PatchJobAsync(
                job.Id,
                new PatchGccV2JobCommand(Stage: "write", Status: "pending", ReleaseClaim: true, Wake: true),
                ct);
            await _events.AppendAsync(
                job.Id,
                _user.UserId,
                "BrandKitAccepted",
                new { jobId = job.Id, brandKitId, viaSibling = job.Id == acceptedViaJobId ? (Guid?)null : acceptedViaJobId },
                ct: ct);
            await _events.AppendAsync(job.Id, _user.UserId, "OutlineApproved", new { jobId = job.Id, auto = true }, ct: ct);
            _wake.Wake(job.Id);
            return updated;
        }

        var outlineJob = await _repo.PatchJobAsync(
            job.Id,
            new PatchGccV2JobCommand(Status: "awaiting_outline_approval", ReleaseClaim: true),
            ct);
        await _events.AppendAsync(
            job.Id,
            _user.UserId,
            "BrandKitAccepted",
            new { jobId = job.Id, brandKitId, viaSibling = job.Id == acceptedViaJobId ? (Guid?)null : acceptedViaJobId },
            ct: ct);
        return outlineJob;
    }

    private static bool IsShortFormContentType(string? contentType)
    {
        var t = (contentType ?? "").Trim().ToLowerInvariant();
        return t is "email" or "social" or "ads" or "image-prompt";
    }

    [HttpPost("jobs/{id:guid}/reject-brandkit")]
    public async Task<ActionResult<GccV2JobDto>> RejectBrandKit(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var job = await _repo.GetJobAsync(id, ct);
        if (job is null || !IsOwner(job.OwnerUserId)) return NotFound();
        if (!string.Equals(job.Status, "awaiting_brandkit_approval", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Job is '{job.Status}', not awaiting brand kit approval." });

        // Revise in place — never fail the job. Operator edits and Accepts to continue.
        if (ResolveCrawlRunId(job) is { } crawlRunId)
        {
            var kits = await _repo.ListBrandKitsByProfileAsync(crawlRunId, ct);
            var kit = kits.FirstOrDefault();
            if (kit is not null)
            {
                await _repo.PatchBrandKitAsync(
                    kit.Id,
                    new PatchGccV2BrandKitCommand(VoiceStatus: "provisional", AcceptedAtUtc: null),
                    ct);
            }
        }

        await _events.AppendAsync(id, _user.UserId, "BrandKitRejected", new
        {
            jobId = id,
            message = "Brand kit acceptance cleared — edit fields and Accept to continue.",
        }, ct: ct);

        var updated = await _repo.PatchJobAsync(
            id,
            new PatchGccV2JobCommand(
                Status: "awaiting_brandkit_approval",
                Error: null,
                ReleaseClaim: true),
            ct);
        return Ok(updated);
    }

    private static string MergeBrandKitEdits(string? existingKitJson, AcceptBrandKitRequest? request)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(existingKitJson) ? "{}" : existingKitJson);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (request is not null && IsBrandKitEditableProp(prop.Name)) continue;
                    prop.WriteTo(writer);
                }

                if (request is not null)
                {
                    if (request.CompanyName is not null)
                        writer.WriteString("companyName", request.CompanyName.Trim());
                    if (request.CompanyDescription is not null)
                        writer.WriteString("companyDescription", request.CompanyDescription.Trim());
                    if (request.PositioningOneLiner is not null)
                        writer.WriteString("positioningOneLiner", request.PositioningOneLiner.Trim());
                    if (request.Tagline is not null)
                        writer.WriteString("tagline", request.Tagline.Trim());
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(existingKitJson) ? "{}" : existingKitJson;
        }
    }

    private static bool IsBrandKitEditableProp(string name) =>
        name.Equals("companyName", StringComparison.OrdinalIgnoreCase)
        || name.Equals("companyDescription", StringComparison.OrdinalIgnoreCase)
        || name.Equals("positioningOneLiner", StringComparison.OrdinalIgnoreCase)
        || name.Equals("tagline", StringComparison.OrdinalIgnoreCase);

    [HttpPut("jobs/{id:guid}/outline")]
    public async Task<ActionResult<object>> PutOutline(Guid id, [FromBody] PutOutlineRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var job = await _repo.GetJobAsync(id, ct);
        if (job is null || !IsOwner(job.OwnerUserId)) return NotFound();
        if (!string.Equals(job.Status, "awaiting_outline_approval", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Job is '{job.Status}' — outline can only be edited while awaiting outline approval." });
        if (request?.Sections is null || request.Sections.Count == 0)
            return BadRequest(new { error = "sections required" });

        var roleValidation = GccV2OutlinePutValidator.ValidatePutOutlineSections(
            request.Sections.Select(s => s.Job).ToList());
        if (roleValidation is not null)
            return BadRequest(new { error = roleValidation });

        var outline = new
        {
            sections = request.Sections.Select((s, i) => new
            {
                key = string.IsNullOrWhiteSpace(s.Key) ? $"section-{i}" : s.Key,
                heading = s.Heading,
                job = s.Job,
                hierarchyChildHeadings = s.HierarchyChildHeadings ?? [],
            }).ToList(),
            hierarchyChildHeadings = request.HierarchyChildHeadings ?? [],
        };
        var outlineJson = JsonSerializer.Serialize(outline, JsonOpts);
        var hierarchyChildHeadingsJson = JsonSerializer.Serialize(request.HierarchyChildHeadings ?? [], JsonOpts);

        try
        {
            await _repo.AddStageResultAsync(
                id,
                new CreateGccV2StageResultCommand("plan", null, outlineJson, 0),
                ct);

            var existing = await _repo.ListOutlinesByBriefAsync(job.BriefId, ct);
            var latest = existing.FirstOrDefault();
            if (latest is not null)
            {
                await _repo.PatchOutlineAsync(
                    latest.Id,
                    new PatchGccV2OutlineCommand(
                        OutlineJson: outlineJson,
                        HierarchyChildHeadingsJson: hierarchyChildHeadingsJson),
                    ct);
            }
            else
            {
                await _repo.CreateOutlineAsync(
                    new CreateGccV2OutlineCommand(
                        job.BriefId,
                        outlineJson,
                        hierarchyChildHeadingsJson),
                    ct);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "PutOutline persistence failed for job {JobId}.", id);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PutOutline failed for job {JobId}.", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Could not save outline." });
        }

        // Save is silent — canvas already has local edits; avoid redundant OutlineReady hub replay.
        return Ok(outline);
    }

    [HttpPost("jobs/{id:guid}/regenerate-outline")]
    public async Task<ActionResult<object>> RegenerateOutline(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var job = await _repo.GetJobAsync(id, ct);
        if (job is null || !IsOwner(job.OwnerUserId)) return NotFound();
        if (!string.Equals(job.Status, "awaiting_outline_approval", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Job is '{job.Status}' — cannot regenerate outline now." });

        var brief = await _repo.GetBriefAsync(job.BriefId, ct);
        if (brief is null) return BadRequest(new { error = "Brief not found." });

        IReadOnlyList<string>? refreshedChildren = null;
        if (!string.IsNullOrWhiteSpace(brief.TargetKeyword)
            || !string.IsNullOrWhiteSpace(TryReadBriefTitle(brief.RawBriefJson)))
        {
            try
            {
                var seeds = new List<string>();
                if (!string.IsNullOrWhiteSpace(brief.TargetKeyword))
                    seeds.Add(brief.TargetKeyword.Trim());
                var title = TryReadBriefTitle(brief.RawBriefJson);
                if (!string.IsNullOrWhiteSpace(title))
                    seeds.Add(title!);

                var hierarchy = GccV2HierarchyToolMatch.TryParseSiteHierarchy(brief.RawBriefJson);
                var match = GccV2HierarchyToolMatch.Match(hierarchy, seeds);
                if (match?.ChildHeadings is { Length: > 0 })
                    refreshedChildren = match.ChildHeadings;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "siteHierarchy refresh failed while regenerating outline for job {JobId}.", id);
            }
        }

        var existingOutlines = await _repo.ListOutlinesByBriefAsync(job.BriefId, ct);
        var variant = existingOutlines.Count;

        var planService = HttpContext.RequestServices.GetRequiredService<GeekAPI.Services.ContentCreatorV2.Plan.GccV2PlanService>();
        var outline = await planService.BuildOutlineAsync(
            job,
            brief,
            ct,
            childHeadingsOverride: refreshedChildren,
            preferSiteStructure: true,
            regenerateVariant: variant);

        await _repo.AddStageResultAsync(
            id,
            new CreateGccV2StageResultCommand("plan", null, JsonSerializer.Serialize(outline, JsonOpts), 0),
            ct);

        var latest = existingOutlines.FirstOrDefault();
        if (latest is not null)
        {
            await _repo.PatchOutlineAsync(
                latest.Id,
                new PatchGccV2OutlineCommand(
                    OutlineJson: JsonSerializer.Serialize(outline, JsonOpts),
                    HierarchyChildHeadingsJson: JsonSerializer.Serialize(outline.HierarchyChildHeadings, JsonOpts)),
                ct);
        }

        await _events.AppendAsync(id, _user.UserId, "OutlineReady", outline, ct: ct);
        return Ok(outline);
    }

    [HttpPost("jobs/{id:guid}/cancel")]
    public async Task<ActionResult<GccV2JobDto>> Cancel(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var job = await _repo.GetJobAsync(id, ct);
        if (job is null || !IsOwner(job.OwnerUserId)) return NotFound();
        if (job.Status is "ready" or "failed" or "canceled")
            return Ok(job);

        await _events.TransitionAsync(
            id,
            _user.UserId,
            new ApplyGccV2JobTransitionCommand(
                Status: "canceled",
                EventType: "JobCanceled",
                EventPayloadJson: JsonSerializer.Serialize(new { jobId = id }, JsonOpts),
                Wake: true),
            ct);

        var updated = await _repo.GetJobAsync(id, ct);
        return Ok(updated!);
    }

    /// <summary>Re-queue a stuck or failed job — clears error, releases claim, wakes worker.</summary>
    [HttpPost("jobs/{id:guid}/retry")]
    public async Task<ActionResult<GccV2JobDto>> RetryJob(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var job = await _repo.GetJobAsync(id, ct);
        if (job is null || !IsOwner(job.OwnerUserId)) return NotFound();

        if (job.Status is not ("pending" or "failed" or "running"))
            return BadRequest(new { error = $"Job status '{job.Status}' is not retryable." });

        var result = await _events.TransitionAsync(
            id,
            _user.UserId,
            new ApplyGccV2JobTransitionCommand(
                Status: "pending",
                Error: "",
                ReleaseClaim: true,
                EventType: "JobRetried",
                EventPayloadJson: JsonSerializer.Serialize(new { jobId = id }, JsonOpts),
                Wake: true),
            ct);

        _wake.Wake(id);

        return Ok(result.Job);
    }

    /// <summary>Retry all non-ready, non-awaiting jobs on a create (e.g. orphaned image-prompt jobs).</summary>
    [HttpPost("creates/{id:guid}/retry-stuck-jobs")]
    public async Task<ActionResult<object>> RetryStuckJobs(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null || !IsOwner(create.OwnerUserId)) return NotFound();

        var jobs = await _repo.ListJobsByCreateAsync(id, ct);
        var retried = new List<Guid>();

        foreach (var job in jobs.Where(GccV2JobRecovery.IsRetryableStuckJob))
        {
            await _events.TransitionAsync(
                job.Id,
                _user.UserId,
                new ApplyGccV2JobTransitionCommand(
                    Status: "pending",
                    Error: "",
                    ReleaseClaim: true,
                    EventType: "JobRetried",
                    EventPayloadJson: JsonSerializer.Serialize(new { jobId = job.Id, bulk = true }, JsonOpts),
                    Wake: true),
                ct);
            _wake.Wake(job.Id);
            retried.Add(job.Id);
        }

        return Ok(new { retriedCount = retried.Count, jobIds = retried });
    }

    private bool IsOwner(string ownerUserId) =>
        _user.IsAuthenticated && string.Equals(ownerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);

    public record CreateCreateRequest(string Title, string? ContentType, JsonElement? SiteSection = null, string? SiteUrl = null);

    /// <summary><c>Brief</c> is stored verbatim as the brief's <c>RawBriefJson</c> (includes operatorTools).
    /// <c>ContentTypes</c> drives one WRITE job per type; falls back to create content type / blog.</summary>
    public record GenerateRequest(
        string? TargetKeyword,
        JsonElement? Brief,
        Guid? ProjectSiteCrawlRunId,
        IReadOnlyList<string>? ContentTypes = null,
        bool? PartnerToolsConfirmed = null);

    private static Guid? ResolveCrawlRunId(GccV2JobDto job) =>
        job.ProjectSiteCrawlRunId ?? job.SiteAnalysisProfileId;

    private static IReadOnlyList<string> ResolveContentTypes(GenerateRequest? request, string? createContentType)
    {
        var raw = request?.ContentTypes?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (raw.Count == 0 && !string.IsNullOrWhiteSpace(createContentType))
            raw.Add(createContentType.Trim().ToLowerInvariant());
        if (raw.Count == 0)
            raw.Add("pillar");

        return raw;
    }

    private static string EnsureBriefContentTypes(string? rawBriefJson, IReadOnlyList<string> contentTypes, string primaryType)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(rawBriefJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawBriefJson);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.NameEquals("contentTypes") || prop.NameEquals("primaryDraft"))
                            continue;
                        prop.WriteTo(writer);
                    }
                }
                catch (JsonException)
                {
                    // ignore corrupt brief; write contentTypes only
                }
            }

            writer.WritePropertyName("contentTypes");
            JsonSerializer.Serialize(writer, contentTypes, JsonOpts);
            writer.WriteString("primaryDraft", primaryType);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public record AcceptBrandKitRequest(
        string? CompanyName = null,
        string? CompanyDescription = null,
        string? PositioningOneLiner = null,
        string? Tagline = null);

    public record PutOutlineRequest(
        IReadOnlyList<PutOutlineSection> Sections,
        IReadOnlyList<string>? HierarchyChildHeadings = null);

    public record PutOutlineSection(
        string? Key,
        string Heading,
        string? Job,
        IReadOnlyList<string>? HierarchyChildHeadings = null);
}
