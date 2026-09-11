using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2StudioLlmExecutorTests
{
    [Fact]
    public void BuildRequest_includes_instructions_example_and_evaluation()
    {
        var request = GccV2StudioLlmExecutor.BuildRequest(
            "Write a brief about {{topic}}",
            "{\"title\":\"Example\"}",
            "Must include a title field.",
            temperature: 0.4,
            model: "gpt-5.4");

        Assert.Equal(0.4, request.Temperature);
        Assert.Equal("gpt-5.4", request.Model);
        Assert.Equal(2, request.Messages.Count);
        Assert.Contains("Write a brief about {{topic}}", request.Messages[1].Content);
        Assert.Contains("{\"title\":\"Example\"}", request.Messages[1].Content);
        Assert.Contains("Must include a title field.", request.Messages[1].Content);
        Assert.Contains("Custom Agent Studio", request.Messages[0].Content);
    }

    [Fact]
    public void BuildArtifactJson_pins_studio_llm_methodology()
    {
        using var inputs = JsonDocument.Parse("""{"topic":"AI readiness"}""");
        var json = GccV2StudioLlmExecutor.BuildArtifactJson(
            "Rendered instructions",
            "example",
            "evaluate me",
            inputs.RootElement,
            "Generated brief body",
            "gpt-5.4");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("customAgentOutput.v1", root.GetProperty("artifactType").GetString());
        Assert.Equal(GccV2StudioLlmExecutor.Methodology, root.GetProperty("methodology").GetString());
        Assert.Equal("Generated brief body", root.GetProperty("generatedOutput").GetString());
        Assert.Equal("gpt-5.4", root.GetProperty("modelUsed").GetString());
        Assert.Equal("AI readiness", root.GetProperty("inputs").GetProperty("topic").GetString());
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public void FirstAllowedModel_reads_first_array_entry()
    {
        Assert.Equal("gpt-5.4", GccV2StudioLlmExecutor.FirstAllowedModel("""["gpt-5.4","gpt-4.1"]"""));
        Assert.Null(GccV2StudioLlmExecutor.FirstAllowedModel("[]"));
        Assert.Null(GccV2StudioLlmExecutor.FirstAllowedModel(null));
    }

    [Fact]
    public void ReadTemperature_clamps_workflow_value()
    {
        using var hot = JsonDocument.Parse("""{"temperature":9}""");
        Assert.Equal(2, GccV2StudioLlmExecutor.ReadTemperature(hot.RootElement));

        using var cool = JsonDocument.Parse("""{"temperature":0.15}""");
        Assert.Equal(0.15, GccV2StudioLlmExecutor.ReadTemperature(cool.RootElement));
    }
}
