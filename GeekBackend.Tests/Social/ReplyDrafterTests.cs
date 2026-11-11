using System.Text.Json;
using GeekAPI.Services.Social.Drafting;
using GeekAPI.Services.Social.LanguageModel;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The drafter writes the reply and returns its text and nothing else. It is held to the same clearance
/// as Triage, is told the voice and the limits the Reviewer enforces, never trims or rewrites what the
/// model wrote, and logs every failure without the comment or the draft.
/// </summary>
public class ReplyDrafterTests
{
    private const string Comment = "PRIVATE-COMMENT what does bookkeeping cost?";

    private static readonly SocialClaudeClearance FacebookCleared =
        SocialClaudeClearance.From(name => name == SocialClaudeClearance.Variable ? SocialPlatforms.Facebook : null);

    private static ReplyDraftRequest Request(
        string category = ReplyCategories.Question,
        string platform = SocialPlatforms.Facebook,
        params string[] approvedLinks) => new()
    {
        Platform = platform,
        AccountDisplayName = "Geek @ Your Spot",
        Category = category,
        CommentText = Comment,
        PostText = "PRIVATE-POST about bookkeeping",
        AuthorDisplayName = "PRIVATE-AUTHOR",
        ApprovedLinks = approvedLinks.ToList(),
    };

    private static (ReplyDrafter Drafter, RecordingLog<ReplyDrafter> Log) Build(FakeModelClient model, SocialClaudeClearance? clearance = null)
    {
        var log = new RecordingLog<ReplyDrafter>();
        return (new ReplyDrafter(model, clearance ?? FacebookCleared, log), log);
    }

    private static void AssertNothingPrivate(RecordingLog<ReplyDrafter> log, string? error)
    {
        foreach (var text in log.Messages.Append(error ?? string.Empty))
            Assert.DoesNotContain("PRIVATE", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_platform_that_is_not_cleared_sends_nothing()
    {
        var (drafter, log) = Build(FakeModelClient.Never(), SocialClaudeClearance.From(_ => null));

        var result = await drafter.DraftAsync(Request());

        Assert.Equal(SocialFailureCodes.ClearanceRequired, result.ErrorCode);
        var line = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains(SocialClaudeClearance.Variable, line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_category_is_refused_before_any_call()
    {
        var (drafter, log) = Build(FakeModelClient.Never());

        var result = await drafter.DraftAsync(Request("rant"));

        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
        Assert.Equal(LogLevel.Error, Assert.Single(log.Entries).Level);
    }

    [Fact]
    public async Task The_reply_is_returned_as_written_apart_from_surrounding_space()
    {
        const string written = "  Thanks for asking — it depends on volume.  ";
        var (drafter, log) = Build(new FakeModelClient(_ => FakeModelClient.Answer(JsonSerializer.Serialize(new { reply = written }))));

        var result = await drafter.DraftAsync(Request());

        Assert.True(result.IsSuccess, result.Error);
        // The em dash stays: the Reviewer refuses it, the drafter does not fix it.
        Assert.Equal(written.Trim(), result.Value!.DraftText);
        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData("""{"reply":""}""")]
    [InlineData("""{"reply":"   "}""")]
    [InlineData("""{"reply":5}""")]
    [InlineData("""{"text":"Hello."}""")]
    public async Task An_answer_with_no_reply_text_is_model_failed(string answer)
    {
        var (drafter, log) = Build(new FakeModelClient(_ => FakeModelClient.Answer(answer)));

        var result = await drafter.DraftAsync(Request());

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Equal(LogLevel.Error, Assert.Single(log.Entries).Level);
        AssertNothingPrivate(log, result.Error);
    }

    [Fact]
    public async Task A_model_call_that_failed_keeps_its_code_and_is_logged()
    {
        var model = new FakeModelClient(_ => Result<JsonElement>.Failure("The Anthropic API could not be reached.", SocialFailureCodes.ModelFailed));
        var (drafter, log) = Build(model);

        var result = await drafter.DraftAsync(Request());

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Single(model.Requests);
        Assert.Equal(LogLevel.Error, Assert.Single(log.Entries).Level);
        AssertNothingPrivate(log, result.Error);
    }

    [Fact]
    public async Task The_prompt_carries_the_voice_the_limit_and_only_the_approved_link()
    {
        var model = new FakeModelClient(_ => FakeModelClient.Answer("""{"reply":"Thank you."}"""));
        var (drafter, _) = Build(model);

        await drafter.DraftAsync(Request(ReplyCategories.Complaint, SocialPlatforms.Facebook, "https://geekatyourspot.com/bookkeeping"));

        var sent = Assert.Single(model.Requests);
        Assert.Equal(ReplyDrafter.ToolName, sent.ToolName);
        Assert.Contains("complaint", sent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Never use an em dash", sent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("never \"SMB\"", sent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains($"At most {ReviewLimits.ReplyCharacters} characters", sent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("https://geekatyourspot.com/bookkeeping", sent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains($"<comment>\n{Comment}\n</comment>", sent.UserMessage.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_approved_link_the_prompt_forbids_every_link()
    {
        var model = new FakeModelClient(_ => FakeModelClient.Answer("""{"reply":"Thank you."}"""));
        var (drafter, _) = Build(model);

        await drafter.DraftAsync(Request(ReplyCategories.Praise));

        Assert.Contains("Do not include any link or web address.", Assert.Single(model.Requests).SystemPrompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReplyCategories.Question)]
    [InlineData(ReplyCategories.Praise)]
    [InlineData(ReplyCategories.Complaint)]
    [InlineData(ReplyCategories.Spam)]
    [InlineData(ReplyCategories.Sensitive)]
    public async Task Every_category_has_its_own_guidance(string category)
    {
        var model = new FakeModelClient(_ => FakeModelClient.Answer("""{"reply":"Thank you."}"""));
        var (drafter, _) = Build(model);

        var result = await drafter.DraftAsync(Request(category));

        Assert.True(result.IsSuccess, result.Error);
    }
}
