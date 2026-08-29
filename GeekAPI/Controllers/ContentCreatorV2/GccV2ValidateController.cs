using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Validate;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

[ApiController]
[Route("api/geek-content-creator-v2/creates/{createId:guid}/validate")]
public class GccV2ValidateController : ControllerBase
{
    private static readonly JsonSerializerOptions ContentDocJson = CreateContentDocJson();

    private static JsonSerializerOptions CreateContentDocJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private readonly ICurrentUserContext _user;
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2WriteService _writeService;
    private readonly GccV2ValidateService _validateService;
    private readonly ILogger<GccV2ValidateController> _logger;

    public GccV2ValidateController(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2WriteService writeService,
        GccV2ValidateService validateService,
        ILogger<GccV2ValidateController> logger)
    {
        _user = user;
        _repo = repo;
        _writeService = writeService;
        _validateService = validateService;
        _logger = logger;
    }

    /// <summary>Re-run one advisory SEO/GEO repair pass on a ready job — operator-triggered from Canvas.</summary>
    [HttpPost("fix-readiness")]
    public async Task<IActionResult> FixReadiness(Guid createId, [FromBody] FixReadinessRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null || !IsOwner(create.OwnerUserId)) return NotFound(new { error = "Create not found." });

        var job = request?.JobId is { } jobId
            ? await _repo.GetJobAsync(jobId, ct)
            : await _repo.GetLatestJobByCreateAsync(createId, ct);
        if (job is null || job.CreateId != createId) return NotFound(new { error = "No job found for this create." });
        if (!IsOwner(job.OwnerUserId)) return Forbid();
        if (!string.Equals(job.Status, "ready", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Fix readiness is only available after the job reaches ready status." });

        GccV2WriteContext wc;
        try
        {
            wc = await _writeService.PrepareAsync(job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fix readiness failed to prepare write context for job {JobId}.", job.Id);
            return Problem("Could not load this job's brief/outline for readiness repair.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var current = await _writeService.ReconstructOutputAsync(job, ct);
        if (current is null)
            return BadRequest(new { error = "No completed draft document found for this job." });

        GccV2ValidateOutcome outcome;
        try
        {
            outcome = await _validateService.RunReadinessFixAsync(wc, _user.UserId, current, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fix readiness failed for job {JobId}.", job.Id);
            return Problem("Readiness repair failed unexpectedly.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var finalDocument = outcome.Final.ToContentDocument();
        var resultJson = JsonSerializer.Serialize(new
        {
            title = outcome.Final.Title,
            metaDescription = outcome.Final.MetaDescription,
            document = finalDocument,
            shipReady = outcome.ShipReady,
            outstandingIssues = outcome.OutstandingIssues,
            repairAttempts = outcome.RepairAttempts,
        }, ContentDocJson);

        await _repo.PatchJobAsync(job.Id, new PatchGccV2JobCommand(ResultJson: resultJson), ct);

        return Ok(new
        {
            shipReady = outcome.ShipReady,
            outstandingIssues = outcome.OutstandingIssues,
            seoScore = outcome.Report.SeoScore,
            geoScore = outcome.Report.GeoScore,
            polishScore = outcome.Report.PolishScore,
            polishShipReady = outcome.Report.PolishShipReady,
            guardrailRestructureCount = outcome.Report.GuardrailRestructureCount,
            guardrailRestructurePhrases = outcome.Report.GuardrailRestructurePhrases ?? Array.Empty<string>(),
            seoChecks = outcome.Report.SeoChecks,
            geoChecks = outcome.Report.GeoChecks,
            overlapHits = outcome.Report.OverlapHits.Select(h => new
            {
                headingA = h.HeadingA,
                headingB = h.HeadingB,
                sharedClaim = h.SharedClaim,
                sectionKeyA = h.SectionKeyA,
                sectionKeyB = h.SectionKeyB,
                repairHint = h.RepairHint,
            }).ToList(),
            repairAttempts = outcome.RepairAttempts,
        });
    }

    private bool IsOwner(string ownerUserId) =>
        _user.IsAuthenticated && string.Equals(ownerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);

    public sealed record FixReadinessRequest(Guid? JobId);
}
