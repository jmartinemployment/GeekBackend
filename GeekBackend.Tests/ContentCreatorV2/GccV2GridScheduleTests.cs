using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2GridScheduleTests
{
    [Fact]
    public void Merge_and_Read_round_trip_schedule_fields()
    {
        var merged = GccV2GridSchedule.Merge("{\"creditsPerRow\":1}", new GccV2GridSchedule.State(
            "daily", true, "sample", 10, "2026-09-10T12:00:00.000Z", null));

        using var doc = JsonDocument.Parse(merged);
        Assert.Equal(1, doc.RootElement.GetProperty("creditsPerRow").GetInt32());
        Assert.True(doc.RootElement.GetProperty("schedule").GetProperty("enabled").GetBoolean());
        Assert.Equal("daily", doc.RootElement.GetProperty("schedule").GetProperty("cadence").GetString());

        var read = GccV2GridSchedule.Read(merged);
        Assert.Equal("daily", read.Cadence);
        Assert.True(read.Enabled);
        Assert.Equal("sample", read.Mode);
        Assert.Equal(10, read.SampleSize);
        Assert.Equal("2026-09-10T12:00:00.000Z", read.NextRunAt);
    }

    [Fact]
    public void IsDue_and_AdvanceNextRunAt_respect_cadence()
    {
        var due = new GccV2GridSchedule.State(
            "weekly", true, "full", 10, "2026-01-01T00:00:00.000Z", null);
        Assert.True(GccV2GridSchedule.IsDue(due, DateTimeOffset.Parse("2026-01-02T00:00:00Z")));
        Assert.False(GccV2GridSchedule.IsDue(due, DateTimeOffset.Parse("2025-12-31T00:00:00Z")));

        var from = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Assert.Equal(from.AddDays(1), GccV2GridSchedule.AdvanceNextRunAt(from, "daily"));
        Assert.Equal(from.AddDays(7), GccV2GridSchedule.AdvanceNextRunAt(from, "weekly"));
        Assert.Equal(from.AddMonths(1), GccV2GridSchedule.AdvanceNextRunAt(from, "monthly"));
        Assert.Null(GccV2GridSchedule.AdvanceNextRunAt(from, "none"));
    }
}
