using GeekAPI.Services.ContentCreatorV2.TaskAgents;
using System.Text.Json;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2TaskAgentNextActionsTests
{
    [Fact]
    public void ForCompletedRun_prepends_create_handoff_before_agent_actions()
    {
        var json = """{"downstream":["faqSet.v1","schemaMarkup.v1"]}""";
        var actions = GccV2TaskAgentNextActions.ForCompletedRun("pillar-outline", "pillarOutline.v1", json);
        Assert.True(actions.Count >= 2);

        var first = JsonSerializer.SerializeToElement(actions[0]);
        Assert.True(first.TryGetProperty("create", out var create));
        Assert.Equal("pillar", create.GetProperty("contentType").GetString());
        Assert.False(first.TryGetProperty("capabilityId", out _));

        Assert.Contains(actions.Skip(1), a =>
        {
            var el = JsonSerializer.SerializeToElement(a);
            return el.TryGetProperty("capabilityId", out var id)
                && id.GetString() == "faq-generator";
        });
    }

    [Fact]
    public void FromCompatibilityJson_still_maps_downstream_agents()
    {
        var actions = GccV2TaskAgentNextActions.FromCompatibilityJson(
            """{"downstream":["comparisonBrief.v1"]}""");
        var only = Assert.Single(actions);
        var el = JsonSerializer.SerializeToElement(only);
        Assert.Equal("comparison-brief", el.GetProperty("capabilityId").GetString());
    }
}
