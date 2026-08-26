using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.ContentCreatorV2.Jobs;
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
    private readonly HttpGeekSeoSiteAnalyzerClient _seo;
    private readonly GccPartnerUrlResearchService _partnerResearch;
    private readonly ILogger<GccV2Controller> _logger;

    public GccV2Controller(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2JobWake wake,
        GccV2JobEventWriter events,
        GccV2BrandKitBuilder brandKitBuilder,
        HttpGeekSeoSiteAnalyzerClient seo,
        GccPartnerUrlResearchService partnerResearch,
        ILogger<GccV2Controller> logger)
    {
        _user = user;
        _repo = repo;
        _wake = wake;
        _events = events;
        _brandKitBuilder = brandKitBuilder;
        _seo = seo;
        _partnerResearch = partnerResearch;
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

    /// <summary>Recent Geek-SEO site analysis profiles for the create-form picker (domain + date, not raw GUIDs).</summary>
    [HttpGet("site-analyzer/profiles/recent")]
    public async Task<IActionResult> ListRecentSiteAnalysisProfiles(
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var bearer = ExtractBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required" });

        var result = await _seo.ListRecentProfilesAsync(bearer, limit, ct);
        if (!result.Ok)
            return StatusCode(result.StatusCode, new { error = result.Error });
        return Ok(result.Value ?? []);
    }

    /// <summary>Profiles for a domain host — same picker source as v1, under the v2 route prefix.</summary>
    [HttpGet("site-analyzer/profiles/by-domain")]
    public async Task<IActionResult> ListSiteAnalysisProfilesByDomain(
        [FromQuery] string domain,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(domain))
            return BadRequest(new { error = "domain required" });
        var bearer = ExtractBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required" });

        var result = await _seo.ListProfilesByDomainAsync(domain, bearer, limit, ct);
        if (!result.Ok)
            return StatusCode(result.StatusCode, new { error = result.Error });
        return Ok(result.Value ?? []);
    }

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
                section = GccGenerateService.ParseSiteSection(siteSectionJson);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Invalid siteSection: {ex.Message}" });
            }
        }

        if (section is null || section.RelatedPages is null || section.RelatedPages.Count == 0)
            return BadRequest(new { error = "siteSection with non-empty relatedPages is required — start from Site Analyzer." });

        var siteUrl = string.IsNullOrWhiteSpace(request.SiteUrl)
            ? section.RelatedPages[0].Url
            : request.SiteUrl.Trim();

        var create = await _repo.CreateCreateAsync(
            new CreateGccV2CreateCommand(
                _user.UserId.ToString("D"),
                request.Title,
                request.ContentType,
                siteSectionJson,
                siteUrl),
            ct);
        return Ok(create);
    }

    [HttpGet("creates")]
    public async Task<ActionResult<IReadOnlyList<GccV2CreateDto>>> ListCreates(CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var creates = await _repo.ListCreatesAsync(_user.UserId.ToString("D"), ct);
        return Ok(creates);
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
    public async Task<ActionResult<IReadOnlyList<GccV2JobDto>>> ListJobsForCreate(Guid id, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null || !IsOwner(create.OwnerUserId)) return NotFound();
        var jobs = await _repo.ListJobsByCreateAsync(id, ct);
        return Ok(jobs);
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

    /// <summary>Creates a brief stub + one pending job per content type, wakes the worker(s).</summary>
    [HttpPost("creates/{id:guid}/generate")]
    public async Task<ActionResult<object>> Generate(Guid id, [FromBody] GenerateRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        if (request?.SiteAnalysisProfileId is not { } profileId || profileId == Guid.Empty)
            return BadRequest(new { error = "siteAnalysisProfileId is required — start from Site Analyzer with a project site URL." });

        SiteSectionContextDto? section;
        try
        {
            section = GccGenerateService.ParseSiteSection(create.SiteSectionJson);
            GccGenerateService.ValidateSiteSectionGate(profileId, section);
            if (section is null || section.RelatedPages is null || section.RelatedPages.Count == 0)
                return BadRequest(new { error = "Create is missing siteSection with relatedPages — start from Site Analyzer." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var bearer = ExtractBearerToken();
        if (string.IsNullOrWhiteSpace(bearer))
            return Unauthorized(new { error = "Bearer token required to build BrandKit from crawl." });

        GccV2BrandKitContent kit;
        try
        {
            kit = await _brandKitBuilder.BuildAsync(
                profileId,
                bearer,
                _user.UserId,
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
        rawBriefJson = await TryMergeHierarchyPlanAsync(rawBriefJson, request, create.Title, ct);
        rawBriefJson = await TryMergePartnerResearchAsync(rawBriefJson, ct);

        var brief = await _repo.CreateBriefAsync(
            new CreateGccV2BriefCommand(id, request?.TargetKeyword, primaryType, RawBriefJson: rawBriefJson),
            ct);

        await _repo.CreateBrandKitAsync(
            new CreateGccV2BrandKitCommand(
                profileId,
                ClientId: null,
                KitJson: JsonSerializer.Serialize(kit, JsonOpts),
                VoiceStatus: "provisional"),
            ct);

        var jobIds = new List<Guid>();
        foreach (var contentType in contentTypes)
        {
            var job = await _repo.CreateJobAsync(
                new CreateGccV2JobCommand(id, _user.UserId.ToString("D"), contentType, brief.Id, profileId),
                ct);
            await _events.AppendAsync(job.Id, _user.UserId, "JobQueued", new { jobId = job.Id, briefId = brief.Id, contentType }, ct: ct);
            _wake.Wake(job.Id);
            jobIds.Add(job.Id);
        }

        return Accepted(new { jobId = jobIds[0], jobIds });
    }

    /// <summary>
    /// Prefetch hierarchy match (+ recommended tools under that heading) onto the brief.
    /// Soft grounding for WRITE: relate to the matched use-case and site-listed partner tools.
    /// Expands keywords (e.g. "Smart Chatbots for Marketing" → "Smart Chatbots") so site H5s
    /// like "Smart Chatbots:" still resolve. Prefers the match with the most tool links.
    /// </summary>
    private async Task<string?> TryMergeHierarchyPlanAsync(
        string? rawBriefJson,
        GenerateRequest? request,
        string? createTitle,
        CancellationToken ct)
    {
        if (request?.SiteAnalysisProfileId is not { } profileId) return rawBriefJson;

        var bearer = ExtractBearerToken();
        if (string.IsNullOrWhiteSpace(bearer)) return rawBriefJson;

        var seeds = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.TargetKeyword))
            seeds.Add(request.TargetKeyword.Trim());
        if (!string.IsNullOrWhiteSpace(createTitle)
            && !seeds.Any(a => a.Equals(createTitle.Trim(), StringComparison.OrdinalIgnoreCase)))
            seeds.Add(createTitle.Trim());
        var titleFromBrief = TryReadBriefTitle(rawBriefJson);
        if (!string.IsNullOrWhiteSpace(titleFromBrief)
            && !seeds.Any(a => a.Equals(titleFromBrief, StringComparison.OrdinalIgnoreCase)))
            seeds.Add(titleFromBrief!);

        var attempts = ExpandKeywordSearchTerms(seeds).ToList();
        if (attempts.Count == 0) return rawBriefJson;

        try
        {
            object? bestPlan = null;
            var bestTools = -1;
            var bestExact = false;
            string? bestHeading = null;
            string? bestTopic = null;

            foreach (var topic in attempts)
            {
                var treesResult = await _seo.FindTreesByKeywordAsync(profileId, topic, bearer, ct);
                if (!treesResult.Ok || treesResult.Value is not { Count: > 0 } trees)
                    continue;

                var matches = GccGenerateService.BuildHierarchyMatchesFromTrees(trees, topic);
                foreach (var candidate in matches.Take(8))
                {
                    var pathLabel = candidate.Path is { Length: > 0 }
                        ? string.Join(" › ", candidate.Path)
                        : null;
                    var tools = GccGenerateService.ExtractToolsFromTrees(
                        trees, topic, candidate.SourcePageUrl, pathLabel);
                    var toolCount = tools.Count;
                    var isExact = string.Equals(candidate.Kind, "exact-heading", StringComparison.OrdinalIgnoreCase);
                    var better = toolCount > bestTools
                        || (toolCount == bestTools && isExact && !bestExact);
                    if (!better)
                        continue;

                    bestTools = toolCount;
                    bestExact = isExact;
                    bestHeading = candidate.MatchedHeading;
                    bestTopic = topic;
                    var toolRows = tools.Select(t => (object)new { name = t.Name, href = t.Href }).ToList();
                    bestPlan = new
                    {
                        matchedHeading = candidate.MatchedHeading,
                        sourcePageUrl = candidate.SourcePageUrl,
                        path = candidate.Path,
                        kind = candidate.Kind,
                        childHeadings = candidate.ChildHeadings,
                        recommendedTools = toolRows,
                        matchTopic = topic,
                    };

                    if (toolCount >= 2 && isExact)
                        break;
                }

                if (bestTools >= 2)
                    break;
            }

            if (bestPlan is null)
                return rawBriefJson;

            _logger.LogInformation(
                "Hierarchy plan for profile {ProfileId}: matched '{Heading}' with {ToolCount} recommended tool(s) via topic '{Topic}'.",
                profileId, bestHeading, bestTools, bestTopic);

            return MergeHierarchyPlanIntoBriefJson(rawBriefJson, bestPlan);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hierarchy-match prefetch failed for profile {ProfileId}; PLAN will use content-type templates instead.", profileId);
            return rawBriefJson;
        }
    }

    /// <summary>
    /// Broadens lookup so "Smart Chatbots for Marketing" also tries "Smart Chatbots" (site H5s
    /// often omit the trailing phrase). Drops stopwords and peels trailing words.
    /// </summary>
    internal static IEnumerable<string> ExpandKeywordSearchTerms(IEnumerable<string> seeds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            foreach (var variant in ExpandOneKeyword(seed))
            {
                var v = variant.Trim();
                if (v.Length < 3) continue;
                if (!seen.Add(v)) continue;
                yield return v;
            }
        }
    }

    private static IEnumerable<string> ExpandOneKeyword(string keyword)
    {
        var k = (keyword ?? "").Trim().TrimEnd(':').Trim();
        if (k.Length == 0) yield break;
        yield return k;

        foreach (var marker in new[] { " for ", " - ", " – ", " — " })
        {
            var idx = k.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                yield return k[..idx].Trim().TrimEnd(':').Trim();
        }

        var words = k.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var n = words.Length - 1; n >= 2; n--)
            yield return string.Join(' ', words.Take(n));

        var significant = words
            .Where(w => w.Length > 2
                && !w.Equals("for", StringComparison.OrdinalIgnoreCase)
                && !w.Equals("the", StringComparison.OrdinalIgnoreCase)
                && !w.Equals("and", StringComparison.OrdinalIgnoreCase)
                && !w.Equals("with", StringComparison.OrdinalIgnoreCase)
                && !w.Equals("from", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (significant.Length >= 2 && significant.Length < words.Length)
            yield return string.Join(' ', significant);
        if (significant.Length >= 2)
            yield return string.Join(' ', significant.Take(2));
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
                    && t.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(t.GetString()))
                    return t.GetString()!.Trim();
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


    /// <summary>
    /// Fetch partner/tool destination pages and attach full page extracts as <c>partnerResearch</c>
    /// on the brief. Soft: failed URLs are skipped; Generate never hard-fails.
    /// </summary>
    private async Task<string?> TryMergePartnerResearchAsync(string? rawBriefJson, CancellationToken ct)
    {
        try
        {
            var hrefs = GccPartnerUrlResearchService.CollectPartnerHrefs(rawBriefJson);
            if (hrefs.Count == 0) return rawBriefJson;

            var pages = await _partnerResearch.FetchAsync(hrefs, ct);
            if (pages.Count == 0)
            {
                _logger.LogWarning(
                    "Partner URL research fetched 0 of {UrlCount} href(s); continuing without partnerResearch.",
                    hrefs.Count);
                return rawBriefJson;
            }

            _logger.LogInformation(
                "Partner URL research stored {PageCount} of {UrlCount} page extract(s) on brief.",
                pages.Count, hrefs.Count);
            return GccPartnerUrlResearchService.MergePartnerResearchIntoBriefJson(rawBriefJson, pages);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Partner URL research failed; continuing without partnerResearch.");
            return rawBriefJson;
        }
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
        if (job.SiteAnalysisProfileId is not { } profileId)
            return BadRequest(new { error = "Job has no siteAnalysisProfileId." });

        var kits = await _repo.ListBrandKitsByProfileAsync(profileId, ct);
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
        if (job.SiteAnalysisProfileId is { } profileId)
        {
            var kits = await _repo.ListBrandKitsByProfileAsync(profileId, ct);
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
                new PatchGccV2OutlineCommand(OutlineJson: outlineJson),
                ct);
        }
        else
        {
            await _repo.CreateOutlineAsync(
                new CreateGccV2OutlineCommand(
                    job.BriefId,
                    outlineJson,
                    JsonSerializer.Serialize(request.HierarchyChildHeadings ?? [], JsonOpts)),
                ct);
        }

        await _events.AppendAsync(id, _user.UserId, "OutlineReady", outline, ct: ct);
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

        var bearer = ExtractBearerToken();
        IReadOnlyList<string>? refreshedChildren = null;
        if (job.SiteAnalysisProfileId is { } profileId
            && !string.IsNullOrWhiteSpace(bearer)
            && !string.IsNullOrWhiteSpace(brief.TargetKeyword))
        {
            try
            {
                var treesResult = await _seo.FindTreesByKeywordAsync(
                    profileId, brief.TargetKeyword!, bearer, ct);
                if (treesResult.Ok && treesResult.Value is { Count: > 0 } trees)
                {
                    var matches = GccGenerateService.BuildHierarchyMatchesFromTrees(trees, brief.TargetKeyword!);
                    var best = matches.FirstOrDefault();
                    if (best?.ChildHeadings is { Length: > 0 })
                        refreshedChildren = best.ChildHeadings;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hierarchy refresh failed while regenerating outline for job {JobId}.", id);
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

        var updated = await _repo.PatchJobAsync(id, new PatchGccV2JobCommand(Status: "canceled"), ct);
        await _events.AppendAsync(id, _user.UserId, "JobCanceled", new { jobId = id }, ct: ct);
        _wake.Wake(id);

        return Ok(updated);
    }

    private bool IsOwner(string ownerUserId) =>
        _user.IsAuthenticated && string.Equals(ownerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);

    public record CreateCreateRequest(string Title, string? ContentType, JsonElement? SiteSection = null, string? SiteUrl = null);

    /// <summary><c>Brief</c> is stored verbatim as the brief's <c>RawBriefJson</c> (includes operatorTools).
    /// <c>ContentTypes</c> drives one WRITE job per type; falls back to create content type / blog.</summary>
    public record GenerateRequest(
        string? TargetKeyword,
        JsonElement? Brief,
        Guid? SiteAnalysisProfileId,
        IReadOnlyList<string>? ContentTypes = null);

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
