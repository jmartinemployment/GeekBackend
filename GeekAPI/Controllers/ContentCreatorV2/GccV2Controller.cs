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
    private readonly ILogger<GccV2Controller> _logger;

    public GccV2Controller(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2JobWake wake,
        GccV2JobEventWriter events,
        GccV2BrandKitBuilder brandKitBuilder,
        HttpGeekSeoSiteAnalyzerClient seo,
        ILogger<GccV2Controller> logger)
    {
        _user = user;
        _repo = repo;
        _wake = wake;
        _events = events;
        _brandKitBuilder = brandKitBuilder;
        _seo = seo;
        _logger = logger;
    }

    [HttpGet("health")]
    public ActionResult<object> Health() =>
        Ok(new
        {
            ok = true,
            product = "geek-content-creator-v2",
            userId = _user.UserId == Guid.Empty ? null : _user.UserId.ToString("D"),
        });

    [HttpPost("creates")]
    public async Task<ActionResult<GccV2CreateDto>> CreateCreate([FromBody] CreateCreateRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "title is required" });

        var create = await _repo.CreateCreateAsync(
            new CreateGccV2CreateCommand(_user.UserId.ToString("D"), request.Title, request.ContentType),
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

    /// <summary>Creates a brief stub + a pending job, wakes the worker, and returns immediately.</summary>
    [HttpPost("creates/{id:guid}/generate")]
    public async Task<ActionResult<object>> Generate(Guid id, [FromBody] GenerateRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(id, ct);
        if (create is null) return NotFound();
        // Plain 403, not Forbid() — that needs IAuthenticationService, which is only registered
        // when GEEK_OAUTH_AUTHORITY is set (see Program.cs); this route must work either way.
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        var rawBriefJson = request?.Brief is { } briefElement
            ? briefElement.GetRawText()
            : null;

        rawBriefJson = await TryMergeHierarchyPlanAsync(rawBriefJson, request, ct);

        var brief = await _repo.CreateBriefAsync(
            new CreateGccV2BriefCommand(id, request?.TargetKeyword, create.ContentType, RawBriefJson: rawBriefJson),
            ct);

        var job = await _repo.CreateJobAsync(
            new CreateGccV2JobCommand(id, _user.UserId.ToString("D"), create.ContentType, brief.Id, request?.SiteAnalysisProfileId),
            ct);

        await _events.AppendAsync(job.Id, _user.UserId, "JobQueued", new { jobId = job.Id, briefId = brief.Id }, ct: ct);

        if (request?.SiteAnalysisProfileId is { } profileId)
        {
            // Best-effort: the PLAN stage re-checks for this kit and announces it via
            // BrandKitReady once the worker picks the job up, so a failure here never blocks
            // Generate — it only means the UI won't see a brand kit summary for this job.
            try
            {
                var kit = await _brandKitBuilder.BuildAsync(profileId, ct);
                await _repo.CreateBrandKitAsync(
                    new CreateGccV2BrandKitCommand(
                        profileId,
                        ClientId: null,
                        KitJson: JsonSerializer.Serialize(kit, JsonOpts),
                        VoiceStatus: kit.VoiceStatus),
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Brand kit build/persist failed for profile {ProfileId} on job {JobId}.", profileId, job.Id);
            }
        }

        _wake.Wake(job.Id);

        return Accepted(new { jobId = job.Id });
    }

    /// <summary>
    /// Best-effort hierarchy-match prefetch: <see cref="GccV2JobWorker"/>'s PLAN stage has no user
    /// bearer for Site Analyzer calls, so grounding PLAN's outline in the site's real page-section
    /// hierarchy can only happen here, at Generate, which has the caller's bearer. Read-only call
    /// through the already-registered <see cref="HttpGeekSeoSiteAnalyzerClient"/> and the public
    /// static <see cref="GccGenerateService.BuildHierarchyMatchesFromTrees"/> — neither is edited.
    /// Any failure, missing profile/keyword/bearer, or empty match silently falls through with the
    /// brief JSON unchanged; PLAN still completes from content-type templates without it.
    /// </summary>
    private async Task<string?> TryMergeHierarchyPlanAsync(string? rawBriefJson, GenerateRequest? request, CancellationToken ct)
    {
        if (request?.SiteAnalysisProfileId is not { } profileId) return rawBriefJson;
        var keyword = request.TargetKeyword;
        if (string.IsNullOrWhiteSpace(keyword)) return rawBriefJson;

        var bearer = ExtractBearerToken();
        if (string.IsNullOrWhiteSpace(bearer)) return rawBriefJson;

        try
        {
            var treesResult = await _seo.FindTreesByKeywordAsync(profileId, keyword, bearer, ct);
            if (!treesResult.Ok || treesResult.Value is not { Count: > 0 } trees)
                return rawBriefJson;

            var matches = GccGenerateService.BuildHierarchyMatchesFromTrees(trees, keyword);
            var best = matches.FirstOrDefault();
            if (best is null) return rawBriefJson;

            var hierarchyPlan = new
            {
                matchedHeading = best.MatchedHeading,
                sourcePageUrl = best.SourcePageUrl,
                path = best.Path,
                kind = best.Kind,
                childHeadings = best.ChildHeadings,
            };

            return MergeHierarchyPlanIntoBriefJson(rawBriefJson, hierarchyPlan);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hierarchy-match prefetch failed for profile {ProfileId}; PLAN will use content-type templates instead.", profileId);
            return rawBriefJson;
        }
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

        return Ok(updated);
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

    public record CreateCreateRequest(string Title, string? ContentType);

    /// <summary><c>Brief</c> is stored verbatim as the brief's <c>RawBriefJson</c> — GeekAPI does
    /// not validate its shape (see <c>brief-catalog.ts</c> in content-creator-v2 for the schema
    /// the frontend sends). <c>SiteAnalysisProfileId</c> is optional; when set, Generate derives
    /// and persists a provisional brand kit for it.</summary>
    public record GenerateRequest(string? TargetKeyword, JsonElement? Brief, Guid? SiteAnalysisProfileId);
}
