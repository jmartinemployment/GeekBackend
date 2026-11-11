using System.Net;
using GeekAPI.Services.Social.Meta;
using GeekAPI.Services.Social.Sending;
using GeekApplication.Models.Social;

namespace GeekBackend.Tests.Social;

/// <summary>
/// Facebook publishing, replying and liking: each is one call with the token in the Authorization
/// header, a request the platform cannot take is refused before any call, and any answer that does not
/// prove Meta refused a write is outcome_unknown, so it is never sent twice.
/// </summary>
public class MetaSendingTests
{
    private const string PageId = "PAGE1";
    private const string TokenValue = "PRIVATE-PAGE-TOKEN";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

    private static readonly SocialAccount Page = new() { Id = Guid.NewGuid(), Platform = SocialPlatforms.Facebook, PlatformAccountId = PageId };
    private static readonly SocialToken Token = new(TokenValue);

    private static MetaGraphClient Graph(ScriptedModelHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.test/") });

    private static ScriptedModelHandler Answer(HttpStatusCode status, string body) =>
        new((_, _) => ScriptedModelHandler.Json(status, body));

    private static ScriptedModelHandler Throws(Exception ex) => new((_, _) => throw ex);

    private static ScriptedModelHandler NoCall() =>
        new((_, _) => throw new InvalidOperationException("No call was expected."));

    private static MetaPublisher Publisher(ScriptedModelHandler handler) => new(Graph(handler), new MetaEngagementReaderTests.FixedClock(Now));

    private static MetaEngagementWriter Writer(ScriptedModelHandler handler) => new(Graph(handler));

    private static SocialPostRequest LinkPost() => new()
    {
        Kind = SocialPostKinds.Link,
        Text = "A new guide on bookkeeping for small businesses.",
        LinkUrl = "https://geekatyourspot.com/bookkeeping",
    };

    private static SocialReplyRequest Reply() => new()
    {
        PlatformPostId = "PAGE1_POST1",
        ParentPlatformCommentId = "COMMENT1",
        Text = "Thanks for asking.",
    };

    private static void AssertTokenOnlyInHeader(ScriptedModelHandler handler)
    {
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal(TokenValue, handler.LastRequest.Headers.Authorization.Parameter);
        Assert.DoesNotContain(TokenValue, handler.LastRequest.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(TokenValue, handler.LastBody ?? string.Empty, StringComparison.Ordinal);
    }

    // ----- Publishing -----

    [Fact]
    public async Task A_link_post_is_one_call_to_the_page_feed_and_returns_the_post_id()
    {
        var handler = Answer(HttpStatusCode.OK, """{"id":"PAGE1_POST9"}""");

        var result = await Publisher(handler).PublishAsync(Page, Token, LinkPost());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("PAGE1_POST9", result.Value!.PlatformPostId);
        Assert.False(result.Value.Scheduled);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith($"/{MetaOptions.GraphVersion}/{PageId}/feed", handler.LastRequest.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("link=https%3A%2F%2Fgeekatyourspot.com%2Fbookkeeping", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("message=", handler.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("published=", handler.LastBody, StringComparison.Ordinal);
        AssertTokenOnlyInHeader(handler);
    }

    [Fact]
    public async Task A_scheduled_post_is_sent_unpublished_with_its_time()
    {
        var handler = Answer(HttpStatusCode.OK, """{"id":"PAGE1_POST9"}""");
        var post = LinkPost();
        post.ScheduledFor = Now.UtcDateTime.AddHours(2);

        var result = await Publisher(handler).PublishAsync(Page, Token, post);

        Assert.True(result.Value!.Scheduled);
        Assert.Contains("published=false", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains($"scheduled_publish_time={Now.AddHours(2).ToUnixTimeSeconds()}", handler.LastBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("image")]
    public async Task A_title_description_or_picture_is_refused_because_facebook_reads_them_from_the_page(string field)
    {
        var post = LinkPost();
        switch (field)
        {
            case "title": post.LinkTitle = "Bookkeeping"; break;
            case "description": post.LinkDescription = "A guide."; break;
            default: post.ImageUrl = "https://geekatyourspot.com/a.png"; break;
        }

        var result = await Publisher(NoCall()).PublishAsync(Page, Token, post);

        Assert.Equal(SocialFailureCodes.NotSupported, result.ErrorCode);
    }

    [Fact]
    public async Task A_post_without_an_https_link_is_refused_before_any_call()
    {
        var post = LinkPost();
        post.LinkUrl = "http://geekatyourspot.com/bookkeeping";

        var result = await Publisher(NoCall()).PublishAsync(Page, Token, post);

        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task An_account_on_another_platform_is_refused()
    {
        var linkedIn = new SocialAccount { Platform = SocialPlatforms.LinkedInOrganization, PlatformAccountId = "urn:li:organization:1" };

        var result = await Publisher(NoCall()).PublishAsync(linkedIn, Token, LinkPost());

        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task A_success_without_an_id_is_outcome_unknown()
    {
        var result = await Publisher(Answer(HttpStatusCode.OK, "{}")).PublishAsync(Page, Token, LinkPost());

        Assert.Equal(SocialFailureCodes.OutcomeUnknown, result.ErrorCode);
    }

    // ----- How a failed write is read -----

    public static TheoryData<int, string, string> WriteAnswers => new()
    {
        { 500, """{"error":{"code":2,"message":"PRIVATE"}}""", SocialFailureCodes.OutcomeUnknown },
        { 500, "not json", SocialFailureCodes.OutcomeUnknown },
        { 200, "not json", SocialFailureCodes.OutcomeUnknown },
        { 400, """{"error":{"code":1,"message":"An unknown error occurred"}}""", SocialFailureCodes.OutcomeUnknown },
        { 400, """{"error":{"code":100,"message":"Invalid parameter"}}""", SocialFailureCodes.PlatformRejected },
        { 400, """{"error":{"code":190,"message":"Expired"}}""", SocialFailureCodes.TokenInvalid },
        { 400, """{"error":{"code":4,"message":"Too many"}}""", SocialFailureCodes.RateLimited },
        { 403, """{"error":{"code":200,"message":"Permissions"}}""", SocialFailureCodes.Forbidden },
    };

    [Theory]
    [MemberData(nameof(WriteAnswers))]
    public async Task A_write_that_meta_did_not_clearly_refuse_is_outcome_unknown(int status, string body, string expected)
    {
        var handler = Answer((HttpStatusCode)status, body);

        var result = await Writer(handler).ReplyAsync(Page, Token, Reply());

        Assert.Equal(expected, result.ErrorCode);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain("PRIVATE", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timeout_on_a_write_is_outcome_unknown()
    {
        var result = await Writer(Throws(new TaskCanceledException("timed out"))).ReplyAsync(Page, Token, Reply());

        Assert.Equal(SocialFailureCodes.OutcomeUnknown, result.ErrorCode);
    }

    [Fact]
    public async Task A_connection_that_broke_after_the_request_left_is_outcome_unknown()
    {
        var broken = new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended.");

        var result = await Writer(Throws(broken)).ReplyAsync(Page, Token, Reply());

        Assert.Equal(SocialFailureCodes.OutcomeUnknown, result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public async Task A_write_that_never_reached_meta_is_platform_unavailable(HttpRequestError error)
    {
        var result = await Writer(Throws(new HttpRequestException(error, "No connection."))).ReplyAsync(Page, Token, Reply());

        Assert.Equal(SocialFailureCodes.PlatformUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task A_read_that_timed_out_is_still_platform_unavailable()
    {
        var reader = new MetaEngagementReader(MetaOptions.From(_ => "x"), Graph(Throws(new TaskCanceledException("timed out"))), new MetaEngagementReaderTests.FixedClock(Now));

        var result = await reader.ListCommentsAsync(Page, Token, "PAGE1_POST1", null);

        Assert.Equal(SocialFailureCodes.PlatformUnavailable, result.ErrorCode);
    }

    // ----- Replying and liking -----

    [Fact]
    public async Task A_reply_is_one_call_under_the_comment_and_returns_the_new_comment_id()
    {
        var handler = Answer(HttpStatusCode.OK, """{"id":"COMMENT1_REPLY1"}""");

        var result = await Writer(handler).ReplyAsync(Page, Token, Reply());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("COMMENT1_REPLY1", result.Value!.PlatformCommentId);
        Assert.EndsWith("/COMMENT1/comments", handler.LastRequest!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("message=Thanks+for+asking.", handler.LastBody, StringComparison.Ordinal);
        AssertTokenOnlyInHeader(handler);
    }

    [Fact]
    public async Task A_reply_with_no_text_is_refused_before_any_call()
    {
        var reply = Reply();
        reply.Text = " ";

        var result = await Writer(NoCall()).ReplyAsync(Page, Token, reply);

        Assert.Equal(SocialFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task A_like_is_one_call_confirmed_by_success_true()
    {
        var handler = Answer(HttpStatusCode.OK, """{"success":true}""");

        var result = await Writer(handler).ReactAsync(Page, Token, new SocialReactionRequest { PlatformTargetId = "COMMENT1" });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.PlatformReactionId);
        Assert.EndsWith("/COMMENT1/likes", handler.LastRequest!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        AssertTokenOnlyInHeader(handler);
    }

    [Fact]
    public async Task A_like_meta_did_not_confirm_is_outcome_unknown()
    {
        var result = await Writer(Answer(HttpStatusCode.OK, "{}")).ReactAsync(Page, Token, new SocialReactionRequest { PlatformTargetId = "COMMENT1" });

        Assert.Equal(SocialFailureCodes.OutcomeUnknown, result.ErrorCode);
    }

    [Fact]
    public async Task A_reaction_a_page_cannot_give_is_refused_before_any_call()
    {
        var result = await Writer(NoCall()).ReactAsync(Page, Token,
            new SocialReactionRequest { PlatformTargetId = "COMMENT1", ReactionType = SocialReactionTypes.Praise });

        Assert.Equal(SocialFailureCodes.NotSupported, result.ErrorCode);
    }
}
