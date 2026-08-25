using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>
/// Sync Canvas edit actions — rewrite/expand/re-tone one already-drafted section on demand, outside
/// the WRITE/VALIDATE job loop. All three share <see cref="GccV2WriteService.RewriteSectionAsync"/>
/// (stage <c>"canvas"</c>) so a Canvas edit is persisted and broadcast exactly like a REPAIR rewrite,
/// just synchronously and user-triggered. Routes are keyed by <c>createId</c> (matching
/// <c>creates/{id}/generate</c>) and resolve to that create's latest job — the frontend already
/// tracks a create's current job id, but sending it isn't required.
/// </summary>
[ApiController]
[Route("api/geek-content-creator-v2/creates/{createId:guid}/canvas")]
public class GccV2CanvasController : ControllerBase
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
    private readonly ILogger<GccV2CanvasController> _logger;

    public GccV2CanvasController(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2WriteService writeService,
        ILogger<GccV2CanvasController> logger)
    {
        _user = user;
        _repo = repo;
        _writeService = writeService;
        _logger = logger;
    }

    [HttpPost("rewrite")]
    public Task<ActionResult<object>> Rewrite(Guid createId, [FromBody] CanvasActionRequest request, CancellationToken ct) =>
        RunActionAsync(createId, request, ct,
            baseInstruction: "Rewrite this section for improved clarity, flow, and quality while keeping the same assigned job and substantive content.",
            eventType: "SectionRewritten");

    [HttpPost("expand")]
    public Task<ActionResult<object>> Expand(Guid createId, [FromBody] CanvasActionRequest request, CancellationToken ct) =>
        RunActionAsync(createId, request, ct,
            baseInstruction: "Expand this section with more depth, concrete detail, or examples, while keeping the same assigned job — do not introduce a new pain/solution claim that duplicates another section.",
            eventType: "SectionExpanded");

    [HttpPost("re-tone")]
    public Task<ActionResult<object>> ReTone(Guid createId, [FromBody] CanvasActionRequest request, CancellationToken ct) =>
        RunActionAsync(createId, request, ct,
            baseInstruction: "Rewrite this section in a different tone/voice only — keep the same substantive content, structure, and assigned job; change phrasing and register, not meaning.",
            eventType: "SectionRetoned");

    private async Task<ActionResult<object>> RunActionAsync(
        Guid createId, CanvasActionRequest request, CancellationToken ct, string baseInstruction, string eventType)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.SectionKey))
            return BadRequest(new { error = "sectionKey is required" });

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound(new { error = "Create not found." });
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        var job = request.JobId is { } explicitJobId
            ? await _repo.GetJobAsync(explicitJobId, ct)
            : await _repo.GetLatestJobByCreateAsync(createId, ct);
        if (job is null || job.CreateId != createId) return NotFound(new { error = "No job found for this create." });
        if (!IsOwner(job.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        GccV2WriteContext wc;
        try
        {
            wc = await _writeService.PrepareAsync(job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Canvas action failed to prepare write context for job {JobId}.", job.Id);
            return Problem("Could not load this job's brief/outline for editing.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var target = await FindSectionAsync(job.Id, request.SectionKey, ct);
        if (target is null) return NotFound(new { error = $"Section '{request.SectionKey}' has not been drafted yet for this job." });

        var notes = string.IsNullOrWhiteSpace(request.Instruction)
            ? baseInstruction
            : $"{baseInstruction} Additional instruction from the user: {request.Instruction}";
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            notes += $" Reference/seed text supplied by the user: {request.Text}";
        }

        GccV2WriteSection updated;
        try
        {
            updated = await _writeService.RewriteSectionAsync(
                wc, _user.UserId, string.Empty, target, notes, ct, stage: "canvas", eventType: eventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Canvas action failed for job {JobId} section {SectionKey}.", job.Id, request.SectionKey);
            return Problem("The rewrite failed unexpectedly.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return Ok(new
        {
            sectionKey = updated.SectionKey,
            heading = updated.Heading,
            job = updated.Job,
            section = updated.Section,
            usedFallbackStub = updated.UsedFallbackStub,
        });
    }

    /// <summary>Most recently persisted <c>write</c>/<c>repair</c>/<c>canvas</c> stage result for this
    /// section key — the section's current, latest content regardless of which stage last touched it.</summary>
    private async Task<GccV2WriteSection?> FindSectionAsync(Guid jobId, string sectionKey, CancellationToken ct)
    {
        var results = await _repo.GetStageResultsAsync(jobId, ct);
        var latest = results
            .Where(r => string.Equals(r.SectionKey, sectionKey, StringComparison.OrdinalIgnoreCase)
                && r.Stage is "write" or "repair" or "canvas")
            .OrderByDescending(r => r.CompletedAtUtc)
            .FirstOrDefault();
        if (latest is null) return null;

        try
        {
            var payload = JsonSerializer.Deserialize<StageSectionPayload>(latest.OutputJson, ContentDocJson);
            if (payload?.Section is null) return null;
            return new GccV2WriteSection(sectionKey, payload.Heading ?? payload.Section.Heading, payload.Job, payload.Section, payload.UsedFallbackStub ?? false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse stage-result payload for job {JobId} section {SectionKey}.", jobId, sectionKey);
            return null;
        }
    }

    private bool IsOwner(string ownerUserId) =>
        _user.IsAuthenticated && string.Equals(ownerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);

    private sealed record StageSectionPayload(string? Heading, string? Job, Section? Section, bool? UsedFallbackStub);

    /// <summary><see cref="Text"/> is optional reference/seed copy the user pastes in (e.g. a
    /// paragraph to work from); <see cref="Instruction"/> is free-text guidance appended to the
    /// action's base instruction. <see cref="JobId"/> is optional — omit to target the create's
    /// latest job.</summary>
    public record CanvasActionRequest(string SectionKey, string? Text, string? Instruction, Guid? JobId);
}
