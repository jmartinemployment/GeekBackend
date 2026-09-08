using System.Text;
using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.Rag;
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
    public const int MaxExactContentCharacters = 100_000;
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
    private readonly GccV2JobEventWriter _events;
    private readonly ILogger<GccV2CanvasController> _logger;

    public GccV2CanvasController(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2WriteService writeService,
        GccV2JobEventWriter events,
        ILogger<GccV2CanvasController> logger)
    {
        _user = user;
        _repo = repo;
        _writeService = writeService;
        _events = events;
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

    /// <summary>
    /// Replaces only the selected section's root body with operator-authored text. No model runs.
    /// Identity, heading, role, evidence/citations, links, nested sections, and image metadata are
    /// copied from the current persisted section. Prior validation is explicitly invalidated.
    /// </summary>
    [HttpPut("section")]
    public async Task<ActionResult<object>> ReplaceSection(
        Guid createId,
        [FromBody] ReplaceSectionRequest? request,
        CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();
        if (request is null || string.IsNullOrWhiteSpace(request.SectionKey))
            return BadRequest(new { error = "sectionKey is required" });
        if (string.IsNullOrWhiteSpace(request.ExactContent))
            return BadRequest(new { error = "exactContent is required" });
        if (request.ExactContent.Length > MaxExactContentCharacters)
            return BadRequest(new { error = $"exactContent cannot exceed {MaxExactContentCharacters} characters" });
        if (request.ExactContent.Contains('\0'))
            return BadRequest(new { error = "exactContent contains an invalid null character" });

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound(new { error = "Create not found." });
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        var job = request.JobId is { } explicitJobId
            ? await _repo.GetJobAsync(explicitJobId, ct)
            : await _repo.GetLatestJobByCreateAsync(createId, ct);
        if (job is null || job.CreateId != createId)
            return NotFound(new { error = "No job found for this create." });
        if (!IsOwner(job.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);
        if (!string.Equals(job.Status, "ready", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Job is '{job.Status}' — direct section editing requires a ready draft." });

        var target = await FindSectionAsync(job.Id, request.SectionKey, ct);
        if (target is null)
            return NotFound(new { error = $"Section '{request.SectionKey}' has not been drafted yet for this job." });

        GccV2WriteContext wc;
        GccV2WriteOutput? current;
        try
        {
            wc = await _writeService.PrepareAsync(job, ct);
            current = await _writeService.ReconstructOutputAsync(job, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct section edit could not reconstruct job {JobId}.", job.Id);
            return Problem("Could not load the durable draft for editing.", statusCode: StatusCodes.Status500InternalServerError);
        }
        if (current is null || !current.AllSections.Any(s =>
                string.Equals(s.SectionKey, target.SectionKey, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { error = "The section exists in history but not in the current durable document." });

        var replacementSection = BuildExactBodyReplacement(target.Section, request.ExactContent);
        var replacement = target with { Section = replacementSection, UsedFallbackStub = false };
        var updatedOutput = current.WithSection(replacement);
        var invalidatedAt = DateTimeOffset.UtcNow;

        try
        {
            await _writeService.PersistOperatorEditAsync(
                wc, _user.UserId, replacement, updatedOutput, ct);
            var mergedResult = MergeDocumentIntoResultJson(job.ResultJson, updatedOutput.ToContentDocument(), invalidatedAt);
            await _repo.PatchJobAsync(
                job.Id,
                new PatchGccV2JobCommand(ResultJson: mergedResult),
                ct);
            var invalidation = new
            {
                reason = "operator-section-edit",
                sectionKey = replacement.SectionKey,
                invalidatedAtUtc = invalidatedAt,
                requiresRevalidation = true,
            };
            await _repo.AddStageResultAsync(
                job.Id,
                new CreateGccV2StageResultCommand(
                    "validation-invalidated",
                    replacement.SectionKey,
                    JsonSerializer.Serialize(invalidation, ContentDocJson),
                    0),
                ct);
            await _events.AppendAsync(
                job.Id,
                _user.UserId,
                "ValidationInvalidated",
                invalidation,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct section edit persistence failed for job {JobId}.", job.Id);
            return Problem("Could not persist the section edit.", statusCode: StatusCodes.Status502BadGateway);
        }

        return Ok(new
        {
            sectionKey = replacement.SectionKey,
            heading = replacement.Heading,
            job = replacement.Job,
            section = replacement.Section,
            wordCount = ContentDocumentText.CountWords(replacement.Section),
            replacement.UsedFallbackStub,
            replacement.Citations,
            replacement.Provenance,
            editKind = "operator-exact-replacement",
            aiGenerated = false,
            validationInvalidated = true,
        });
    }

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
            citations = updated.Citations,
            provenance = updated.Provenance,
        });
    }

    /// <summary>Most recently persisted <c>write</c>/<c>repair</c>/<c>canvas</c> stage result for this
    /// section key — the section's current, latest content regardless of which stage last touched it.</summary>
    private async Task<GccV2WriteSection?> FindSectionAsync(Guid jobId, string sectionKey, CancellationToken ct)
    {
        var results = await _repo.GetStageResultsAsync(jobId, ct);
        var latest = results
            .Where(r => string.Equals(r.SectionKey, sectionKey, StringComparison.OrdinalIgnoreCase)
                && r.Stage is "write" or "repair" or "canvas" or "operator-edit" or "final-synthesis")
            .OrderByDescending(r => r.CompletedAtUtc)
            .FirstOrDefault();
        if (latest is null) return null;

        try
        {
            var payload = JsonSerializer.Deserialize<StageSectionPayload>(latest.OutputJson, ContentDocJson);
            if (payload?.Section is null) return null;
            return new GccV2WriteSection(
                latest.SectionKey ?? sectionKey,
                payload.Heading ?? payload.Section.Heading,
                payload.Job,
                payload.Section,
                payload.UsedFallbackStub ?? false,
                payload.Citations,
                payload.Provenance);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse stage-result payload for job {JobId} section {SectionKey}.", jobId, sectionKey);
            return null;
        }
    }

    private bool IsOwner(string ownerUserId) =>
        _user.IsAuthenticated && string.Equals(ownerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);

    internal static Section BuildExactBodyReplacement(Section current, string exactContent)
    {
        var normalized = exactContent.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        var paragraphs = normalized
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(text => (Paragraph)new TextParagraph([new Run(text.Trim())]))
            .ToList();
        return current with { Paragraphs = paragraphs };
    }

    internal static string MergeDocumentIntoResultJson(
        string? existingResultJson,
        ContentDocument document,
        DateTimeOffset invalidatedAt)
    {
        using var existing = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(existingResultJson) ? "{}" : existingResultJson);
        if (existing.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Job result must be a JSON object.");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in existing.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("document")
                    || prop.NameEquals("validationState"))
                    continue;
                prop.WriteTo(writer);
            }
            writer.WritePropertyName("document");
            JsonSerializer.Serialize(writer, document, ContentDocJson);
            writer.WritePropertyName("validationState");
            JsonSerializer.Serialize(writer, new
            {
                valid = false,
                reason = "operator-section-edit",
                invalidatedAtUtc = invalidatedAt,
            }, ContentDocJson);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed record StageSectionPayload(
        string? Heading,
        string? Job,
        Section? Section,
        bool? UsedFallbackStub,
        IReadOnlyList<RagCitationDto>? Citations = null,
        GccV2GenerationProvenance? Provenance = null);

    /// <summary><see cref="Text"/> is optional reference/seed copy the user pastes in (e.g. a
    /// paragraph to work from); <see cref="Instruction"/> is free-text guidance appended to the
    /// action's base instruction. <see cref="JobId"/> is optional — omit to target the create's
    /// latest job.</summary>
    public record CanvasActionRequest(string SectionKey, string? Text, string? Instruction, Guid? JobId);

    public record ReplaceSectionRequest(string SectionKey, string ExactContent, Guid? JobId);
}
