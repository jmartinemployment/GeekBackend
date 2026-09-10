using GeekAPI.Services.ContentCreatorV2;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2GridPipelineBindingTests
{
    [Fact]
    public void Merge_and_read_preserve_pipeline_binding_beside_schedule()
    {
        var withSchedule = GccV2GridSchedule.Merge("{\"creditsPerRow\":1}", new GccV2GridSchedule.State(
            "daily", true, "sample", 10, "2026-01-01T00:00:00Z", null));
        var merged = GccV2GridPipelineBinding.Merge(withSchedule, new GccV2GridPipelineBinding.State(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            "11111111-2222-3333-4444-555555555555"));

        var binding = GccV2GridPipelineBinding.Read(merged);
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", binding.PipelineDefinitionId);
        Assert.Equal("11111111-2222-3333-4444-555555555555", binding.LastPipelineRunId);

        var schedule = GccV2GridSchedule.Read(merged);
        Assert.Equal("daily", schedule.Cadence);
        Assert.True(schedule.Enabled);
    }

    [Fact]
    public void Merge_clears_binding_when_detached()
    {
        var seeded = GccV2GridPipelineBinding.Merge("{}", new GccV2GridPipelineBinding.State(
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            "11111111-2222-3333-4444-555555555555"));
        var cleared = GccV2GridPipelineBinding.Merge(seeded, new GccV2GridPipelineBinding.State(null, null));
        var binding = GccV2GridPipelineBinding.Read(cleared);
        Assert.Null(binding.PipelineDefinitionId);
        Assert.Null(binding.LastPipelineRunId);
    }
}
