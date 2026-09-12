using GeekAPI.Services.ContentCreatorV2.Pipelines;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2PipelineStagePolicyTests
{
    [Fact]
    public void Default_aeo_template_covers_all_five_lifecycle_stages()
    {
        var error = GccV2PipelineStagePolicy.ValidateStagesJson(
            GccV2PipelineStagePolicy.DefaultAeoTemplateStagesJson(), out var stages);
        Assert.Null(error);
        Assert.Equal(7, stages.Count);
        Assert.Contains(stages, stage => stage.Key == "approve-publish" && stage.Kind == "approval");
        Assert.Contains(stages, stage => stage.Key == "optimize-roi"
            && stage.CapabilityId == "roi-business-calculator");
        Assert.All(GccV2PipelineStagePolicy.LifecycleStages, lifecycle =>
            Assert.Contains(stages, stage => stage.Lifecycle == lifecycle));
        Assert.False(string.IsNullOrWhiteSpace(
            GccV2PipelineStagePolicy.Digest(
                GccV2PipelineStagePolicy.DefaultAeoTemplateStagesJson(), "{}")));
    }

    [Fact]
    public void Rejects_missing_lifecycle_stage()
    {
        var stages = """
            [
              {"key":"plan","lifecycle":"plan","kind":"task-agent","capabilityId":"query-planner","displayName":"Plan"},
              {"key":"create","lifecycle":"create","kind":"task-agent","capabilityId":"faq-generator","displayName":"Create"},
              {"key":"adapt","lifecycle":"adapt","kind":"handoff","handoff":"canvas","displayName":"Adapt"},
              {"key":"activate","lifecycle":"activate","kind":"handoff","handoff":"publish","displayName":"Activate"}
            ]
            """;
        var error = GccV2PipelineStagePolicy.ValidateStagesJson(stages, out _);
        Assert.Contains("five lifecycle stages", error);
    }
}
