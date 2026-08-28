using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Jobs;

/// <summary>
/// Recovery thresholds for orphaned <c>pending</c> jobs and stale claim failures.
/// Mirrors the one-shot expired-lease scan — no polling loop.
/// </summary>
public static class GccV2JobRecovery
{
    /// <summary>Skip waking jobs touched within this window at startup (may still be mid-notify).</summary>
    public static readonly TimeSpan PendingWakeGrace = TimeSpan.FromSeconds(30);

    /// <summary>Pending job with no progress longer than this is considered stuck.</summary>
    public static readonly TimeSpan StaleClaimThreshold = TimeSpan.FromMinutes(3);

    public const int MaxStaleClaimWakeAttempts = 5;

    public static bool IsActiveLease(GccV2JobDto job, DateTimeOffset now) =>
        job.LeaseUntilUtc is not null
        && job.LeaseUntilUtc > now
        && !string.IsNullOrWhiteSpace(job.ClaimedByInstanceId);

    public static bool IsStalePending(GccV2JobDto job, DateTimeOffset now) =>
        string.Equals(job.Status, "pending", StringComparison.OrdinalIgnoreCase)
        && now - (job.UpdatedAtUtc ?? job.CreatedAtUtc) >= StaleClaimThreshold;

    public static bool ShouldWakeAtStartup(GccV2JobDto job, DateTimeOffset now) =>
        string.Equals(job.Status, "pending", StringComparison.OrdinalIgnoreCase)
        && now - (job.UpdatedAtUtc ?? job.CreatedAtUtc) >= PendingWakeGrace;

    public static bool IsRetryableStuckJob(GccV2JobDto job) =>
        string.Equals(job.Status, "pending", StringComparison.OrdinalIgnoreCase)
        || string.Equals(job.Status, "failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(job.Status, "running", StringComparison.OrdinalIgnoreCase);
}
