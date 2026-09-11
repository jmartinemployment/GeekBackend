using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2EditDistanceMetricsTests
{
    [Fact]
    public void NormalizedDistance_identical_is_zero()
    {
        Assert.Equal(0, GccV2EditDistanceMetrics.NormalizedDistance(
            "  Hello   World  ", "hello world"));
    }

    [Fact]
    public void NormalizedDistance_empty_is_null()
    {
        Assert.Null(GccV2EditDistanceMetrics.NormalizedDistance("", "text"));
        Assert.Null(GccV2EditDistanceMetrics.NormalizedDistance(null, "text"));
    }

    [Fact]
    public void NormalizedDistance_small_edit_is_low()
    {
        var distance = GccV2EditDistanceMetrics.NormalizedDistance(
            "Reliable AI content readiness",
            "Reliable AI content readiness score");
        Assert.NotNull(distance);
        Assert.InRange(distance!.Value, 0.01, 0.35);
    }

    [Fact]
    public void NormalizedDistance_total_rewrite_is_high()
    {
        var distance = GccV2EditDistanceMetrics.NormalizedDistance(
            "aaaa",
            "bbbb");
        Assert.Equal(1, distance);
    }

    [Fact]
    public void Combine_averages_samples()
    {
        var aggregate = GccV2EditDistanceMetrics.Combine([0.1, 0.3, 0.5]);
        Assert.Equal(3, aggregate.Samples);
        Assert.Equal(0.3, aggregate.MeanEditDistance);
    }
}
