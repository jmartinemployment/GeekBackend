using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2StudioEvaluatorTests
{
    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["topic"],
          "properties": { "topic": { "type": "string" } }
        }
        """;

    [Fact]
    public void EvaluateSuite_requires_min_cases_and_all_pass()
    {
        using var good = JsonDocument.Parse("""{"topic":"AI readiness"}""");
        using var bad = JsonDocument.Parse("""{}""");
        var suite = GccV2StudioEvaluator.EvaluateSuite(
            [
                ("c1", "Good", good.RootElement.Clone()),
                ("c2", "Bad", bad.RootElement.Clone()),
            ],
            minTestCases: 1,
            Schema,
            "Topic {{inputs.topic}} for {{outcome}}",
            "Agent",
            "Write a brief",
            "{\"summary\":\"x\"}",
            "Must be JSON");

        Assert.False(suite.Valid);
        Assert.Equal(1, suite.PassedCount);
        Assert.Equal(2, suite.CaseCount);
    }

    [Fact]
    public void EvaluateSuite_passes_when_threshold_met()
    {
        using var good = JsonDocument.Parse("""{"topic":"AI readiness"}""");
        var suite = GccV2StudioEvaluator.EvaluateSuite(
            [("c1", "Good", good.RootElement.Clone())],
            minTestCases: 1,
            Schema,
            "Topic {{inputs.topic}} for {{outcome}}",
            "Agent",
            "Write a brief",
            "{\"summary\":\"x\"}",
            "Must be JSON");

        Assert.True(suite.Valid);
        Assert.Equal(1, suite.PassedCount);
        Assert.Contains("passed", suite.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadTestCases_parses_workflow_array()
    {
        using var workflow = JsonDocument.Parse("""
            {
              "testCases": [
                { "id": "a", "name": "Alpha", "input": { "topic": "one" } },
                { "id": "b", "name": "Beta" }
              ]
            }
            """);
        var cases = GccV2StudioEvaluator.ReadTestCases(workflow.RootElement);
        Assert.Equal(2, cases.Count);
        Assert.Equal("a", cases[0].Id);
        Assert.Equal("one", cases[0].Input.GetProperty("topic").GetString());
        Assert.Equal(JsonValueKind.Object, cases[1].Input.ValueKind);
    }
}
