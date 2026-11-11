using System.Text.Json;
using GeekAPI.Services.Social.Listener;
using GeekAPI.Services.Social.Meta;
using GeekAPI.Services.Social.Orchestration;
using GeekAPI.Services.Social.Publishing;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests.Social;

/// <summary>
/// Resync stores a missed comment as the same event a webhook would have, so it is stored once, and
/// wakes the orchestrator only for new ones. The post writer asks for one text per platform, checks the
/// answer in code, and shapes each post the way its platform takes it.
/// </summary>
public class SocialResyncAndPostWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid AccountId = Guid.NewGuid();

    private sealed class FakeReader(Func<string, string?, Result<SocialCommentPage>> read) : ISocialEngagementReader
    {
        public List<(string PostId, string? Cursor)> Reads { get; } = new();

        public string Platform => SocialPlatforms.Facebook;

        public Result<SocialHandshakeResponse> AnswerHandshake(SocialHandshakeRequest request) => throw new InvalidOperationException();

        public Result<IReadOnlyList<EngagementEvent>> ParseWebhook(SocialWebhookRequest request) => throw new InvalidOperationException();

        public Task<Result<SocialCommentPage>> ListCommentsAsync(SocialAccount account, SocialToken token, string platformPostId, string? cursor, CancellationToken ct = default)
        {
            Reads.Add((platformPostId, cursor));
            return Task.FromResult(read(platformPostId, cursor));
        }
    }

    private static (SocialResync Resync, FakeSocialStore Store, SocialWake Wake, RecordingLog<SocialResync> Log) Resync(
        FakeReader reader, Func<EngagementEvent, Result<bool>> record)
    {
        var store = new FakeSocialStore
        {
            OnGetAccount = id => Result<SocialAccount>.Success(new SocialAccount { Id = id, Platform = SocialPlatforms.Facebook, PlatformAccountId = "PAGE1" }),
            OnListPosts = (_, _) => Result<IReadOnlyList<SocialPost>>.Success(new[]
            {
                new SocialPost { PlatformPostId = "PAGE1_POST1", PublishedAt = Now.UtcDateTime.AddDays(-1) },
                new SocialPost { PlatformPostId = "PAGE1_SCHEDULED", PublishedAt = null },
            }),
            OnRecordEvent = record,
        };
        var services = new ServiceCollection().AddKeyedSingleton<ISocialEngagementReader>(SocialPlatforms.Facebook, reader).BuildServiceProvider();
        var wake = new SocialWake();
        var log = new RecordingLog<SocialResync>();
        return (new SocialResync(services, store, SocialSendStepTests.FakeTokens.Good(), wake, new MetaEngagementReaderTests.FixedClock(Now), log), store, wake, log);
    }

    private static SocialComment Comment(string id) => new()
    {
        PlatformCommentId = id, Text = "PRIVATE-COMMENT", AuthorPlatformId = "U1", AuthorDisplayName = "PRIVATE-AUTHOR", CreatedAt = Now.UtcDateTime.AddHours(-2),
    };

    [Fact]
    public async Task Resync_stores_each_comment_under_the_webhook_s_key_and_wakes_only_for_new_ones()
    {
        var reader = new FakeReader((_, cursor) => Result<SocialCommentPage>.Success(cursor is null
            ? new SocialCommentPage { Comments = { Comment("C1") }, NextCursor = "AFTER1" }
            : new SocialCommentPage { Comments = { Comment("C2") } }));
        var (resync, store, wake, _) = Resync(reader, ev => Result<bool>.Success(ev.PlatformCommentId == "C2"));

        var result = await resync.ResyncAsync(AccountId, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value!.PostsRead);
        Assert.Equal(2, result.Value.CommentsRead);
        Assert.Equal(1, result.Value.Stored);
        Assert.Equal(new[] { ("PAGE1_POST1", (string?)null), ("PAGE1_POST1", "AFTER1") }, reader.Reads);
        Assert.Equal(MetaEngagementReader.CreatedCommentEventId("C1"), store.RecordedEvents[0].PlatformEventId);
        Assert.All(store.RecordedEvents, ev => Assert.Equal(EngagementKinds.CommentCreated, ev.Kind));
        Assert.True(wake.Reader.TryRead(out var woken));
        Assert.Equal(store.RecordedEvents[1].Id, woken);
        Assert.False(wake.Reader.TryRead(out _));
    }

    [Fact]
    public async Task A_read_that_fails_part_way_says_how_many_were_stored_and_is_logged()
    {
        var reader = new FakeReader((_, _) => Result<SocialCommentPage>.Failure("Meta could not be reached.", SocialFailureCodes.PlatformUnavailable));
        var (resync, _, _, log) = Resync(reader, _ => Result<bool>.Success(true));

        var result = await resync.ResyncAsync(AccountId, CancellationToken.None);

        Assert.Equal(SocialFailureCodes.PlatformUnavailable, result.ErrorCode);
        Assert.Contains("0 comment(s) were stored", result.Error, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Error, Assert.Single(log.Entries).Level);
    }

    // ----- The post writer -----

    private static PostCopyRequest Article(params string[] platforms) => new()
    {
        ArticleUrl = "https://geekatyourspot.com/blog/bookkeeping",
        ArticleTitle = "Bookkeeping for small businesses",
        ArticleSummary = "How to keep the books in an hour a week.",
        Platforms = platforms.ToList(),
    };

    private static string Posts(params (string Platform, string Text)[] posts) =>
        JsonSerializer.Serialize(new { posts = posts.Select(p => new { platform = p.Platform, text = p.Text }) });

    [Fact]
    public async Task Each_platform_gets_its_text_in_the_shape_it_takes()
    {
        var model = new FakeModelClient(_ => FakeModelClient.Answer(Posts(
            (SocialPlatforms.Facebook, "  A new guide.  "), (SocialPlatforms.LinkedInOrganization, "Our new guide."))));
        var writer = new PostWriter(model, new RecordingLog<PostWriter>());

        var result = await writer.WriteAsync(Article(SocialPlatforms.Facebook, SocialPlatforms.LinkedInOrganization));

        Assert.True(result.IsSuccess, result.Error);
        var facebook = result.Value!.Copies.Single(c => c.Platform == SocialPlatforms.Facebook).Post;
        Assert.Equal("A new guide.", facebook.Text);
        Assert.Equal("https://geekatyourspot.com/blog/bookkeeping", facebook.LinkUrl);
        Assert.Null(facebook.LinkTitle);
        var linkedIn = result.Value.Copies.Single(c => c.Platform == SocialPlatforms.LinkedInOrganization).Post;
        Assert.Equal("Bookkeeping for small businesses", linkedIn.LinkTitle);

        var sent = Assert.Single(model.Requests);
        Assert.Equal(PostWriter.ToolName, sent.ToolName);
        Assert.Contains("Never use an em dash", sent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("https://geekatyourspot.com/blog/bookkeeping", sent.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains($"At most {ReviewLimits.PostCharacters} characters", sent.SystemPrompt, StringComparison.Ordinal);
    }

    public static TheoryData<string> UnsoundPosts => new()
    {
        """{"posts":[]}""",
        """{"posts":[{"platform":"facebook","text":"   "}]}""",
        """{"posts":[{"platform":"instagram","text":"Hello"}]}""",
        """{"posts":[{"platform":"facebook","text":"A"},{"platform":"facebook","text":"B"}]}""",
        """{"text":"Hello"}""",
    };

    [Theory]
    [MemberData(nameof(UnsoundPosts))]
    public async Task An_answer_missing_a_platform_or_naming_another_is_model_failed_and_logged(string answer)
    {
        var log = new RecordingLog<PostWriter>();
        var writer = new PostWriter(new FakeModelClient(_ => FakeModelClient.Answer(answer)), log);

        var result = await writer.WriteAsync(Article(SocialPlatforms.Facebook));

        Assert.Equal(SocialFailureCodes.ModelFailed, result.ErrorCode);
        Assert.Equal(LogLevel.Error, Assert.Single(log.Entries).Level);
    }

    [Theory]
    [InlineData("http://geekatyourspot.com/blog", "Title", "Summary", SocialPlatforms.Facebook)]
    [InlineData("https://geekatyourspot.com/blog", " ", "Summary", SocialPlatforms.Facebook)]
    [InlineData("https://geekatyourspot.com/blog", "Title", "", SocialPlatforms.Facebook)]
    [InlineData("https://geekatyourspot.com/blog", "Title", "Summary", "myspace")]
    [InlineData("https://geekatyourspot.com/blog", "Title", "Summary", SocialPlatforms.Instagram)]
    public async Task A_request_it_cannot_write_for_is_refused_before_any_call(string url, string title, string summary, string platform)
    {
        var writer = new PostWriter(FakeModelClient.Never(), new RecordingLog<PostWriter>());

        var result = await writer.WriteAsync(new PostCopyRequest
        {
            ArticleUrl = url, ArticleTitle = title, ArticleSummary = summary, Platforms = new List<string> { platform },
        });

        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
    }
}
