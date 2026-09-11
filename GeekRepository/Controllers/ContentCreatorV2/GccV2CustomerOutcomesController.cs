using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/roi/outcomes")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2CustomerOutcomesController(ContentCreatorV2DbContext db) : ControllerBase
{
    private static readonly HashSet<string> AllowedEvidenceStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "customer-reported",
        "telemetry-measured",
        "modeled",
        "experimental",
        "independently-audited",
    };

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2CustomerOutcome>>> List(
        [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required.");
        var rows = await db.GccV2CustomerOutcomes.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId)
            .OrderByDescending(x => x.PeriodEnd)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2CustomerOutcome>> Get(
        Guid id, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required.");
        var row = await db.GccV2CustomerOutcomes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2CustomerOutcome>> Create(CreateCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId))
            return BadRequest("ownerUserId is required.");
        if (string.IsNullOrWhiteSpace(command.Title))
            return BadRequest("title is required.");
        if (string.IsNullOrWhiteSpace(command.MetricDefinition))
            return BadRequest("metricDefinition is required.");
        if (string.IsNullOrWhiteSpace(command.Source))
            return BadRequest("source is required.");
        if (command.PeriodEnd < command.PeriodStart)
            return BadRequest("periodEnd must be on or after periodStart.");

        var evidence = string.IsNullOrWhiteSpace(command.EvidenceStatus)
            ? "customer-reported"
            : command.EvidenceStatus.Trim();
        if (!AllowedEvidenceStatuses.Contains(evidence))
            return BadRequest("evidenceStatus must be customer-reported, telemetry-measured, modeled, experimental, or independently-audited.");

        var confidence = command.AttributionConfidence;
        if (confidence is < 0 or > 1)
            return BadRequest("attributionConfidence must be between 0 and 1.");

        var now = DateTimeOffset.UtcNow;
        var row = new GccV2CustomerOutcome
        {
            OwnerUserId = command.OwnerUserId.Trim(),
            Title = command.Title.Trim(),
            MetricDefinition = command.MetricDefinition.Trim(),
            PeriodStart = command.PeriodStart,
            PeriodEnd = command.PeriodEnd,
            Baseline = NullIfBlank(command.Baseline),
            Denominator = NullIfBlank(command.Denominator),
            ObservedValue = NullIfBlank(command.ObservedValue),
            Source = command.Source.Trim(),
            EvidenceStatus = evidence.ToLowerInvariant(),
            AttributionMethod = NullIfBlank(command.AttributionMethod),
            AttributionConfidence = confidence,
            WorkflowVersionsJson = string.IsNullOrWhiteSpace(command.WorkflowVersionsJson)
                ? "[]"
                : command.WorkflowVersionsJson.Trim(),
            GeneratedCount = command.GeneratedCount,
            AcceptedCount = command.AcceptedCount,
            PublishedCount = command.PublishedCount,
            RejectedCount = command.RejectedCount,
            ReviewMinutes = command.ReviewMinutes,
            Notes = NullIfBlank(command.Notes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required.");
        var row = await db.GccV2CustomerOutcomes
            .SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        if (row is null) return NotFound();
        db.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed record CreateCommand(
        string OwnerUserId,
        string Title,
        string MetricDefinition,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string? Baseline,
        string? Denominator,
        string? ObservedValue,
        string Source,
        string? EvidenceStatus,
        string? AttributionMethod,
        double? AttributionConfidence,
        string? WorkflowVersionsJson,
        int? GeneratedCount,
        int? AcceptedCount,
        int? PublishedCount,
        int? RejectedCount,
        int? ReviewMinutes,
        string? Notes);
}
