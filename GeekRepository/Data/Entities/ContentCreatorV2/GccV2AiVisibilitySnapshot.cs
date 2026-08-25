namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// A point-in-time "AI-visibility readiness" reading for a create — dual SEO/GEO scores
/// (<c>GcwSeoAnalyzer</c> + <c>GccV2GeoAnalyzer</c>) plus published CMS URLs
/// (<see cref="GccV2PublishRecord"/>), snapshotted together so Canvas can show one score without
/// re-deriving it on every render. Not a live ChatGPT/Perplexity citation tracker — no external
/// calls. A create/job can have several of these over time (re-generate, re-publish, manual
/// refresh) — this is an append-only history, not a 1:1 mirror of "current" state.
/// </summary>
public class GccV2AiVisibilitySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreateId { get; set; }

    /// <summary>The job whose completed <c>ResultJson</c> this snapshot was scored from. Null only
    /// if a snapshot is ever built without a resolvable job (shouldn't happen in practice — the
    /// service requires a completed job to build one).</summary>
    public Guid? JobId { get; set; }

    public string OwnerUserId { get; set; } = string.Empty;

    /// <summary>Overall 0-100 readiness score — an average of the SEO and GEO scores in
    /// <see cref="ReportJson"/>, not a new independent metric.</summary>
    public int Score { get; set; }

    /// <summary>Full report: SEO score, GEO score + named checks/fix hints, overlap/ship-ready
    /// summary, and published CMS URLs at snapshot time. Shape is <c>GccV2AiVisibilityService</c>'s
    /// internal <c>AiVisibilityReport</c> record, serialized — treat as opaque JSON here.</summary>
    public string ReportJson { get; set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
