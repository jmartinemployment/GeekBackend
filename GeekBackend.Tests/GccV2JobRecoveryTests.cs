using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Jobs;

namespace GeekBackend.Tests;

public sealed class GccV2JobRecoveryTests
{
    [Fact]
    public void IsActiveLease_true_when_lease_in_future()
    {
        var now = DateTimeOffset.UtcNow;
        var job = new GccV2JobDto(
            Guid.NewGuid(),
            "pillar",
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid(),
            "write",
            "running",
            1,
            null,
            null,
            "worker-1",
            now,
            now.AddMinutes(5),
            null,
            now,
            now,
            null);

        Assert.True(GccV2JobRecovery.IsActiveLease(job, now));
    }

    [Fact]
    public void IsStalePending_true_after_threshold()
    {
        var now = DateTimeOffset.UtcNow;
        var job = new GccV2JobDto(
            Guid.NewGuid(),
            "image-prompt",
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid(),
            "write",
            "pending",
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            now.AddMinutes(-10),
            now.AddMinutes(-4),
            null);

        Assert.True(GccV2JobRecovery.IsStalePending(job, now));
    }

    [Fact]
    public void ShouldWakeAtStartup_skips_recent_pending()
    {
        var now = DateTimeOffset.UtcNow;
        var recent = new GccV2JobDto(
            Guid.NewGuid(),
            "image-prompt",
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid(),
            "write",
            "pending",
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            now,
            now.AddSeconds(-5),
            null);

        Assert.False(GccV2JobRecovery.ShouldWakeAtStartup(recent, now));
    }

    [Theory]
    [InlineData("pending", true)]
    [InlineData("failed", true)]
    [InlineData("running", true)]
    [InlineData("ready", false)]
    [InlineData("awaiting_outline_approval", false)]
    public void IsRetryableStuckJob_matches_status(string status, bool expected)
    {
        var job = new GccV2JobDto(
            Guid.NewGuid(),
            "pillar",
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid(),
            "plan",
            status,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null);

        Assert.Equal(expected, GccV2JobRecovery.IsRetryableStuckJob(job));
    }
}
