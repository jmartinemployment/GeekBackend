using GeekAPI.Services.ContentCreatorV2;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2GridRoiBindingTests
{
    [Fact]
    public void Merge_and_read_preserve_roi_pin_beside_schedule()
    {
        var withSchedule = GccV2GridSchedule.Merge("{\"creditsPerRow\":1}", new GccV2GridSchedule.State(
            "daily", true, "sample", 10, "2026-01-01T00:00:00Z", null));
        var merged = GccV2GridRoiBinding.Merge(withSchedule, new GccV2GridRoiBinding.State(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            "11111111-2222-3333-4444-555555555555",
            "roiProjection.v1",
            42.5,
            "2026-09-10T12:00:00Z"));

        var binding = GccV2GridRoiBinding.Read(merged);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", binding.RunId);
        Assert.Equal("11111111-2222-3333-4444-555555555555", binding.ArtifactVersionId);
        Assert.Equal("roiProjection.v1", binding.ArtifactType);
        Assert.Equal(42.5, binding.ExpectedRoiPercent);
        Assert.Equal("daily", GccV2GridSchedule.Read(merged).Cadence);
    }

    [Fact]
    public void Merge_clears_roi_pin()
    {
        var seeded = GccV2GridRoiBinding.Merge("{}", new GccV2GridRoiBinding.State(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            "11111111-2222-3333-4444-555555555555",
            "roiProjection.v1",
            10,
            null));
        var cleared = GccV2GridRoiBinding.Merge(seeded, null);
        var binding = GccV2GridRoiBinding.Read(cleared);
        Assert.Null(binding.RunId);
        Assert.Null(binding.ArtifactVersionId);
    }
}
