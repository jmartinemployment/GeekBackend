namespace GeekRepository.Data.Entities.ContentCreatorV2;

/// <summary>
/// A single content-generation job. Advances through <see cref="Stage"/> (plan → write →
/// validate → repair → done) while <see cref="Status"/> tracks execution/lease state.
/// Workers claim via <see cref="ClaimedByInstanceId"/>/<see cref="LeaseUntilUtc"/> — no poll loop,
/// only wakes from <c>NOTIFY gcc_v2_job</c> / in-process Channel.
/// </summary>
public class GccV2Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ContentType { get; set; } = "blog";

    /// <summary>Empty for dummy/no-brief jobs (Phase 3 smoke test).</summary>
    public Guid BriefId { get; set; } = Guid.Empty;

    public string OwnerUserId { get; set; } = string.Empty;
    public Guid CreateId { get; set; }

    /// <summary>Set at generate-time when the create was started with a site analysis profile —
    /// lets the PLAN stage look up/announce the derived <see cref="GccV2BrandKit"/> without the
    /// worker needing its own copy of the request.</summary>
    public Guid? SiteAnalysisProfileId { get; set; }

    /// <summary>plan | write | validate | repair | done</summary>
    public string Stage { get; set; } = "plan";

    /// <summary>pending | running | awaiting_outline_approval | ready | failed | canceled</summary>
    public string Status { get; set; } = "pending";

    public int AttemptCount { get; set; }
    public string? ResultJson { get; set; }
    public string? Error { get; set; }

    public string? ClaimedByInstanceId { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }

    public int? TokensUsed { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
