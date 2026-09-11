namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// Evidence-aware customer outcome record — metric definition + period + provenance,
/// not a vendor marketing claim or cash ROI coefficient.
/// </summary>
public sealed class GccV2CustomerOutcome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MetricDefinition { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string? Baseline { get; set; }
    public string? Denominator { get; set; }
    public string? ObservedValue { get; set; }
    public string Source { get; set; } = string.Empty;
    /// <summary>customer-reported | telemetry-measured | modeled | experimental | independently-audited</summary>
    public string EvidenceStatus { get; set; } = "customer-reported";
    public string? AttributionMethod { get; set; }
    public double? AttributionConfidence { get; set; }
    public string WorkflowVersionsJson { get; set; } = "[]";
    public int? GeneratedCount { get; set; }
    public int? AcceptedCount { get; set; }
    public int? PublishedCount { get; set; }
    public int? RejectedCount { get; set; }
    public int? ReviewMinutes { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
