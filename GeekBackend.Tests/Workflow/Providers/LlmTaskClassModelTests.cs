using GeekAPI.Services.Workflow.Providers;

namespace GeekBackend.Tests.Workflow.Providers;

/// <summary>
/// Which model serves which kind of call.
///
/// One setting drove every OpenAI call -- 37 structured extraction calls, the lede, the body,
/// metadata, image prompts, the FAQ -- so a single bad value took the whole pipeline out at once
/// and read as a data shortage for two hours, and trying a cheaper model meant trying it on the
/// prose as well.
/// </summary>
public class LlmTaskClassModelTests
{
    private static OpenAiOptions Options(string? extraction = null, string? utility = null) => new()
    {
        Model = "gpt-4o",
        ExtractionModel = extraction ?? string.Empty,
        UtilityModel = utility ?? string.Empty,
    };

    [Fact]
    public void WithNothingSetEveryTaskKeepsTheSingleConfiguredModel()
    {
        // The previous behaviour exactly, so adding the settings changes nothing until they are set.
        var options = Options();

        Assert.Equal("gpt-4o", options.ResolveModel(LlmTaskClass.Writing));
        Assert.Equal("gpt-4o", options.ResolveModel(LlmTaskClass.Extraction));
        Assert.Equal("gpt-4o", options.ResolveModel(LlmTaskClass.Utility));
    }

    [Fact]
    public void ExtractionAndUtilityTakeTheirOwnModelWhileWritingKeepsThePrimary()
    {
        var options = Options(extraction: "gpt-4o-mini", utility: "gpt-4o-mini");

        Assert.Equal("gpt-4o", options.ResolveModel(LlmTaskClass.Writing));
        Assert.Equal("gpt-4o-mini", options.ResolveModel(LlmTaskClass.Extraction));
        Assert.Equal("gpt-4o-mini", options.ResolveModel(LlmTaskClass.Utility));
    }

    [Fact]
    public void ABlankSettingFallsBackRatherThanSendingAnEmptyModelName()
    {
        // Railway variables arrive as strings, and "" is how one gets cleared. Passing that through
        // would fail every call with an unhelpful provider error.
        var options = Options(extraction: "   ");

        Assert.Equal("gpt-4o", options.ResolveModel(LlmTaskClass.Extraction));
    }

    [Fact]
    public void ACallThatSaysNothingGetsTheWritingModelNotTheCheapOne()
    {
        // The default must be the good model: a new prompt added later, with no task class on it,
        // should silently get better output rather than silently get worse.
        var request = new ChatCompletionRequest([new ChatMessage(ChatRole.User, "x")]);

        Assert.Equal(LlmTaskClass.Writing, request.TaskClass);
    }
}
