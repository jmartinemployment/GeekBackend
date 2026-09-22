using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>
/// Transparent projected-versus-observed ROI helpers. Observed metrics are workflow outcomes
/// (counts/rates), not dollar claims.
/// </summary>
[ApiController]
[Route("api/geek-content-creator-v2/roi")]
public sealed class GccV2RoiController(
    ICurrentUserContext user,
    HttpGccV2Repository repo) : ControllerBase
{
    private const string ContractVersion = "gcc-roi-observed.v4";

    private string Owner => user.UserId.ToString("D");

    [HttpGet("observed")]
    public async Task<ActionResult<object>> Observed(
        [FromQuery] int lookbackDays = 90, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var days = Math.Clamp(lookbackDays, 1, 365);
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var runs = await repo.ListTaskRunsAsync(Owner, ct: ct);
        var window = runs.Where(r => r.CreatedAtUtc >= since).ToList();

        var generated = window.Count;
        var accepted = window.Count(r =>
            string.Equals(r.Status, "succeeded", StringComparison.OrdinalIgnoreCase));
        var rejected = window.Count(r =>
            string.Equals(r.Status, "failed", StringComparison.OrdinalIgnoreCase));
        var cancelled = window.Count(r =>
            string.Equals(r.Status, "cancelled", StringComparison.OrdinalIgnoreCase));

        var canvasStats = await CollectCanvasStatsAsync(since, ct);

        var reviewMinutes = window
            .Where(r => r.CompletedAtUtc is not null)
            .Select(r => Math.Max(0, (r.CompletedAtUtc!.Value - r.CreatedAtUtc).TotalMinutes))
            .Sum();

        var qualitySamples = window
            .Where(r => string.Equals(r.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            .SelectMany(r => (r.Artifacts ?? []).Select(artifact =>
            {
                var latest = (artifact.Versions ?? [])
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault();
                return latest is null
                    ? null
                    : GccV2ArtifactQualityMetrics.FromPayload(latest.PayloadJson, latest.ValidationState);
            }))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();
        var quality = GccV2ArtifactQualityMetrics.Combine(qualitySamples);
        var editDistance = GccV2EditDistanceMetrics.Combine(canvasStats.EditDistances);

        var empty = generated == 0 && accepted == 0 && canvasStats.PublishedCount == 0;
        return Ok(new
        {
            contractVersion = ContractVersion,
            observed = new
            {
                generatedCount = generated,
                acceptedCount = accepted,
                publishedCount = canvasStats.PublishedCount,
                rejectedCount = rejected,
                cancelledCount = cancelled,
                reviewMinutes = (int)Math.Round(reviewMinutes),
                lookbackDays = days,
                periodLabel = $"Last {days} days (TaskRuns + Canvas publishes)",
                source = empty ? "empty" : "telemetry",
                groundednessRate = quality.GroundednessRate,
                groundedHits = quality.GroundedHits,
                groundedTotal = quality.GroundedTotal,
                schemaValidityRate = quality.SchemaValidityRate,
                schemaValidHits = quality.SchemaValidHits,
                schemaValidTotal = quality.SchemaValidTotal,
                qualityRunsSampled = quality.RunsSampled,
                meanEditDistance = editDistance.MeanEditDistance,
                editDistanceSamples = editDistance.Samples,
                notes = new[]
                {
                    "Counts are owner-scoped workflow outcomes, not cash ROI.",
                    "generated/accepted/rejected come from TaskRuns in the lookback window.",
                    "publishedCount counts Canvas asset versions whose latest status is published in the window.",
                    "reviewMinutes approximates wall-clock TaskRun duration for terminal runs.",
                    "groundednessRate uses sections.grounded and claim/FAQ verificationStatus=supported.",
                    "schemaValidityRate uses artifact validationState plus schemaMarkup validation[].valid.",
                    "meanEditDistance compares first vs latest Canvas asset Summary text when both exist.",
                    quality.Message,
                    editDistance.Message,
                },
            },
        });
    }

    private async Task<(int PublishedCount, List<double> EditDistances)> CollectCanvasStatsAsync(
        DateTimeOffset since, CancellationToken ct)
    {
        var projects = await repo.ListCanvasProjectsAsync(Owner, ct);
        var published = 0;
        var distances = new List<double>();
        foreach (var item in projects)
        {
            var project = await repo.GetCanvasProjectAsync(item.Id, Owner, ct);
            if (project is null) continue;
            foreach (var asset in project.Assets)
            {
                var ordered = asset.Versions.OrderBy(v => v.VersionNumber).ToList();
                if (ordered.Count == 0) continue;
                var latest = ordered[^1];
                if (string.Equals(latest.Status, "published", StringComparison.OrdinalIgnoreCase)
                    && latest.CreatedAtUtc >= since)
                {
                    published++;
                }

                if (ordered.Count < 2) continue;
                if (latest.CreatedAtUtc < since) continue;
                var first = ordered[0];
                var distance = GccV2EditDistanceMetrics.NormalizedDistance(first.Summary, latest.Summary);
                if (distance is { } value) distances.Add(value);
            }
        }

        return (published, distances);
    }

    [HttpGet("outcomes")]
    public async Task<ActionResult<object>> ListOutcomes(CancellationToken ct = default)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var rows = await repo.ListCustomerOutcomesAsync(Owner, ct);
        return Ok(new
        {
            contractVersion = "gcc-roi-outcomes.v1",
            outcomes = rows.Select(Summary).ToList(),
        });
    }

    [HttpPost("outcomes")]
    public async Task<ActionResult<object>> CreateOutcome(
        [FromBody] CreateOutcomeBody body, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (body is null) return BadRequest(new { error = "Body is required." });
        if (string.IsNullOrWhiteSpace(body.Title))
            return BadRequest(new { error = "title is required." });
        if (string.IsNullOrWhiteSpace(body.MetricDefinition))
            return BadRequest(new { error = "metricDefinition is required." });
        if (string.IsNullOrWhiteSpace(body.Source))
            return BadRequest(new { error = "source is required." });
        if (!DateOnly.TryParse(body.PeriodStart, out var periodStart)
            || !DateOnly.TryParse(body.PeriodEnd, out var periodEnd))
            return BadRequest(new { error = "periodStart and periodEnd must be ISO dates (yyyy-MM-dd)." });

        var saved = await repo.CreateCustomerOutcomeAsync(
            new CreateGccV2CustomerOutcomeCommand(
                Owner,
                body.Title,
                body.MetricDefinition,
                periodStart,
                periodEnd,
                body.Baseline,
                body.Denominator,
                body.ObservedValue,
                body.Source,
                body.EvidenceStatus,
                body.AttributionMethod,
                body.AttributionConfidence,
                body.WorkflowVersionsJson,
                body.GeneratedCount,
                body.AcceptedCount,
                body.PublishedCount,
                body.RejectedCount,
                body.ReviewMinutes,
                body.Notes),
            ct);
        return Ok(new
        {
            contractVersion = "gcc-roi-outcomes.v1",
            outcome = Summary(saved),
        });
    }

    [HttpDelete("outcomes/{id:guid}")]
    public async Task<IActionResult> DeleteOutcome(Guid id, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        await repo.DeleteCustomerOutcomeAsync(id, Owner, ct);
        return NoContent();
    }

    private static object Summary(GccV2CustomerOutcomeDto row) => new
    {
        id = row.Id,
        title = row.Title,
        metricDefinition = row.MetricDefinition,
        periodStart = row.PeriodStart.ToString("yyyy-MM-dd"),
        periodEnd = row.PeriodEnd.ToString("yyyy-MM-dd"),
        baseline = row.Baseline,
        denominator = row.Denominator,
        observedValue = row.ObservedValue,
        source = row.Source,
        evidenceStatus = row.EvidenceStatus,
        attributionMethod = row.AttributionMethod,
        attributionConfidence = row.AttributionConfidence,
        workflowVersionsJson = row.WorkflowVersionsJson,
        generatedCount = row.GeneratedCount,
        acceptedCount = row.AcceptedCount,
        publishedCount = row.PublishedCount,
        rejectedCount = row.RejectedCount,
        reviewMinutes = row.ReviewMinutes,
        notes = row.Notes,
        createdAtUtc = row.CreatedAtUtc,
        updatedAtUtc = row.UpdatedAtUtc,
    };

    public sealed record CreateOutcomeBody(
        string Title,
        string MetricDefinition,
        string PeriodStart,
        string PeriodEnd,
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
