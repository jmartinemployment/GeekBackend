using System.Net;
using System.Security.Cryptography;
using System.Text;
using GeekAPI.Services.Social.Meta;
using GeekApplication.Models.Social;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The Listener's Facebook half. The delivery fixtures are written from Meta's Page feed reference
/// (docs/PLATFORM-CAPABILITIES.md 2a) and not captured from a live delivery: whether a
/// development-mode app receives them at all is open item M3, settled by one real comment.
/// </summary>
public class MetaEngagementReaderTests
{
    private const string Secret = "test-app-secret-not-real";
    private const string VerifyToken = "test-verify-token-not-real";
    private const string PageId = "PAGE1";
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);

    private static MetaOptions Options(string? secret = Secret, string? verify = VerifyToken) =>
        new() { AppSecret = secret, VerifyToken = verify };

    private static MetaEngagementReader Reader(MetaOptions? options = null, StubHandler? handler = null) =>
        new(options ?? Options(), new MetaGraphClient(new HttpClient(handler ?? new StubHandler(_ => throw new InvalidOperationException("No call expected.")))
        {
            BaseAddress = new Uri("https://graph.facebook.test/"),
        }), new FixedClock(Now));

    private static SocialWebhookRequest Signed(string body, string secret = Secret)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var digest = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes)).ToLowerInvariant();
        return new SocialWebhookRequest
        {
            RawBody = bytes,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Hub-Signature-256"] = $"sha256={digest}" },
        };
    }

    private static string Feed(string value, long entryTime = 1731153600) =>
        $$"""{"object":"page","entry":[{"id":"{{PageId}}","time":{{entryTime}},"changes":[{"field":"feed","value":{{value}}}]}]}""";

    private static string Comment(string verb, string commentId = "C1", string postId = "POST1", string parentId = "POST1", string? sender = """{"id":"U1","name":"Test Person"}""", string message = "How much does this cost?") =>
        $$"""{"from":{{sender ?? "null"}},"item":"comment","comment_id":"{{commentId}}","post_id":"{{postId}}","verb":"{{verb}}","parent_id":"{{parentId}}","created_time":1731153590,"message":"{{message}}"}""";

    private static EngagementEvent OneEvent(string body)
    {
        var parsed = Reader().ParseWebhook(Signed(body));
        Assert.True(parsed.IsSuccess, parsed.Error);
        return Assert.Single(parsed.Value!);
    }

    // The handshake

    [Fact]
    public void The_handshake_echoes_the_challenge_when_the_verify_token_matches()
    {
        var answer = Reader().AnswerHandshake(Handshake("subscribe", VerifyToken, "1158201444"));

        Assert.True(answer.IsSuccess);
        Assert.Equal("1158201444", answer.Value!.Body);
        Assert.Equal("text/plain", answer.Value.ContentType);
    }

    [Fact]
    public void The_handshake_refuses_a_wrong_verify_token_as_forbidden()
    {
        var answer = Reader().AnswerHandshake(Handshake("subscribe", "not-the-token", "1158201444"));

        Assert.False(answer.IsSuccess);
        Assert.Equal(SocialFailureCodes.Forbidden, answer.ErrorCode);
    }

    [Fact]
    public void The_handshake_with_no_verify_token_configured_is_not_configured_and_never_answers()
    {
        var answer = Reader(Options(verify: null)).AnswerHandshake(Handshake("subscribe", "anything", "1158201444"));

        Assert.Equal(SocialFailureCodes.NotConfigured, answer.ErrorCode);
        Assert.Contains(MetaOptions.VerifyTokenVariable, answer.Error);
    }

    [Theory]
    [InlineData("unsubscribe", "1158201444")]
    [InlineData("subscribe", "")]
    [InlineData("subscribe", "line\nbreak")]
    public void The_handshake_refuses_a_wrong_mode_and_a_challenge_that_is_not_a_plain_string(string mode, string challenge)
    {
        var answer = Reader().AnswerHandshake(Handshake(mode, VerifyToken, challenge));

        Assert.Equal(SocialFailureCodes.InvalidRequest, answer.ErrorCode);
    }

    [Fact]
    public void The_handshake_makes_no_call_to_Meta()
    {
        // The reader's HTTP handler throws if it is used, so a call would fail this test.
        Assert.True(Reader().AnswerHandshake(Handshake("subscribe", VerifyToken, "1")).IsSuccess);
    }

    // The signature

    [Fact]
    public void A_delivery_signed_with_the_app_secret_is_accepted()
    {
        Assert.True(Reader().ParseWebhook(Signed(Feed(Comment("add")))).IsSuccess);
    }

    [Fact]
    public void A_delivery_signed_with_another_secret_is_refused_and_yields_no_events()
    {
        var parsed = Reader().ParseWebhook(Signed(Feed(Comment("add")), "someone-elses-secret"));

        Assert.Equal(SocialFailureCodes.InvalidSignature, parsed.ErrorCode);
        Assert.Null(parsed.Value);
    }

    [Fact]
    public void A_body_changed_after_signing_is_refused()
    {
        var request = Signed(Feed(Comment("add")));
        request.RawBody = Encoding.UTF8.GetBytes(Feed(Comment("add", message: "changed after signing")));

        Assert.Equal(SocialFailureCodes.InvalidSignature, Reader().ParseWebhook(request).ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=")]
    [InlineData("sha256=zz-not-hex")]
    [InlineData("sha1=abcdef")]
    [InlineData("abcdef")]
    public void A_missing_or_malformed_signature_header_is_refused(string? header)
    {
        var request = Signed(Feed(Comment("add")));
        request.Headers = header is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Hub-Signature-256"] = header };

        Assert.Equal(SocialFailureCodes.InvalidSignature, Reader().ParseWebhook(request).ErrorCode);
    }

    [Fact]
    public void With_no_app_secret_configured_no_delivery_is_accepted()
    {
        var parsed = Reader(Options(secret: null)).ParseWebhook(Signed(Feed(Comment("add"))));

        Assert.Equal(SocialFailureCodes.NotConfigured, parsed.ErrorCode);
        Assert.Contains(MetaOptions.AppSecretVariable, parsed.Error);
    }

    [Fact]
    public void The_signature_header_is_found_whatever_its_case()
    {
        var request = Signed(Feed(Comment("add")));
        var digest = request.Headers["X-Hub-Signature-256"];
        request.Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["x-hub-signature-256"] = digest };

        Assert.True(Reader().ParseWebhook(request).IsSuccess);
    }

    // What a change becomes

    [Fact]
    public void A_new_top_level_comment_is_a_created_event_with_its_text_author_and_page()
    {
        var created = OneEvent(Feed(Comment("add")));

        Assert.Equal(EngagementKinds.CommentCreated, created.Kind);
        Assert.Equal(SocialPlatforms.Facebook, created.Platform);
        Assert.Equal(PageId, created.PlatformAccountId);
        Assert.Equal("C1", created.PlatformCommentId);
        Assert.Equal("POST1", created.PlatformPostId);
        Assert.Null(created.ParentPlatformCommentId);
        Assert.Equal("U1", created.AuthorPlatformId);
        Assert.Equal("Test Person", created.AuthorDisplayName);
        Assert.Equal("How much does this cost?", created.Text);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731153590).UtcDateTime, created.OccurredAt);
        Assert.Equal(Now.UtcDateTime, created.ReceivedAt);
        Assert.Equal(DateTimeKind.Utc, created.OccurredAt.Kind);
    }

    [Fact]
    public void A_reply_names_the_comment_it_answers_and_a_top_level_comment_does_not()
    {
        var reply = OneEvent(Feed(Comment("add", commentId: "C2", parentId: "C1")));

        Assert.Equal("C1", reply.ParentPlatformCommentId);
    }

    [Theory]
    [InlineData("add", EngagementKinds.CommentCreated)]
    [InlineData("edit", EngagementKinds.CommentEdited)]
    [InlineData("edited", EngagementKinds.CommentEdited)]
    [InlineData("remove", EngagementKinds.CommentDeleted)]
    [InlineData("delete", EngagementKinds.CommentDeleted)]
    [InlineData("hide", EngagementKinds.CommentHidden)]
    [InlineData("unhide", EngagementKinds.CommentUnhidden)]
    public void Each_documented_comment_verb_maps_to_one_event_kind(string verb, string kind) =>
        Assert.Equal(kind, OneEvent(Feed(Comment(verb))).Kind);

    [Fact]
    public void A_comment_verb_Meta_does_not_document_fails_the_delivery_and_does_not_guess()
    {
        var parsed = Reader().ParseWebhook(Signed(Feed(Comment("archive"))));

        Assert.Equal(SocialFailureCodes.InvalidRequest, parsed.ErrorCode);
        Assert.Contains("archive", parsed.Error);
    }

    [Fact]
    public void A_comment_with_no_text_or_no_author_still_becomes_an_event()
    {
        var created = OneEvent(Feed(
            """{"item":"comment","comment_id":"C9","post_id":"POST1","verb":"add","parent_id":"POST1","created_time":1731153590}"""));

        Assert.Null(created.Text);
        Assert.Null(created.AuthorPlatformId);
        Assert.Null(created.AuthorDisplayName);
    }

    [Fact]
    public void A_comment_without_its_id_fails_the_delivery()
    {
        var parsed = Reader().ParseWebhook(Signed(Feed(
            """{"item":"comment","post_id":"POST1","verb":"add","parent_id":"POST1","created_time":1731153590}""")));

        Assert.Equal(SocialFailureCodes.InvalidRequest, parsed.ErrorCode);
    }

    [Fact]
    public void A_change_with_no_time_of_its_own_takes_the_entry_time_and_with_neither_fails()
    {
        var own = OneEvent("""{"object":"page","entry":[{"id":"PAGE1","time":1731153600,"changes":[{"field":"feed","value":{"item":"comment","comment_id":"C1","post_id":"POST1","verb":"add","parent_id":"POST1"}}]}]}""");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731153600).UtcDateTime, own.OccurredAt);

        var none = Reader().ParseWebhook(Signed(
            """{"object":"page","entry":[{"id":"PAGE1","changes":[{"field":"feed","value":{"item":"comment","comment_id":"C1","post_id":"POST1","verb":"add","parent_id":"POST1"}}]}]}"""));
        Assert.Equal(SocialFailureCodes.InvalidRequest, none.ErrorCode);
    }

    [Fact]
    public void Reactions_are_added_or_removed_with_the_type_upper_cased()
    {
        var added = OneEvent(Feed(
            """{"from":{"id":"U2","name":"Test Person"},"item":"reaction","reaction_type":"love","verb":"add","post_id":"POST1","parent_id":"POST1","created_time":1731153590}"""));
        var removed = OneEvent(Feed(
            """{"from":{"id":"U2","name":"Test Person"},"item":"reaction","reaction_type":"love","verb":"remove","post_id":"POST1","parent_id":"POST1","created_time":1731153595}"""));

        Assert.Equal(EngagementKinds.ReactionAdded, added.Kind);
        Assert.Equal("LOVE", added.ReactionType);
        Assert.Equal(EngagementKinds.ReactionRemoved, removed.Kind);
        Assert.Null(added.PlatformCommentId);
        Assert.NotEqual(added.PlatformEventId, removed.PlatformEventId);
    }

    [Fact]
    public void A_reaction_on_a_comment_names_that_comment()
    {
        var onComment = OneEvent(Feed(
            """{"from":{"id":"U2"},"item":"reaction","reaction_type":"like","verb":"add","post_id":"POST1","parent_id":"C1","comment_id":"C1","created_time":1731153590}"""));

        Assert.Equal("C1", onComment.PlatformCommentId);
    }

    [Fact]
    public void A_reaction_with_no_type_or_no_person_fails_the_delivery()
    {
        Assert.Equal(SocialFailureCodes.InvalidRequest, Reader().ParseWebhook(Signed(Feed(
            """{"from":{"id":"U2"},"item":"reaction","verb":"add","post_id":"POST1","parent_id":"POST1","created_time":1731153590}"""))).ErrorCode);
        Assert.Equal(SocialFailureCodes.InvalidRequest, Reader().ParseWebhook(Signed(Feed(
            """{"item":"reaction","reaction_type":"like","verb":"add","post_id":"POST1","parent_id":"POST1","created_time":1731153590}"""))).ErrorCode);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("post")]
    [InlineData("photo")]
    [InlineData("share")]
    public void A_feed_change_that_is_not_a_comment_or_a_reaction_stores_nothing_and_is_not_an_error(string item)
    {
        var parsed = Reader().ParseWebhook(Signed(Feed($$"""{"item":"{{item}}","verb":"add","post_id":"POST1","created_time":1731153590}""")));

        Assert.True(parsed.IsSuccess);
        Assert.Empty(parsed.Value!);
    }

    [Fact]
    public void A_change_in_another_field_than_feed_is_ignored()
    {
        var parsed = Reader().ParseWebhook(Signed(
            """{"object":"page","entry":[{"id":"PAGE1","time":1,"changes":[{"field":"mention","value":{"item":"comment"}}]}]}"""));

        Assert.True(parsed.IsSuccess);
        Assert.Empty(parsed.Value!);
    }

    [Fact]
    public void One_delivery_with_several_changes_yields_one_event_each_across_entries()
    {
        var body = $$"""
            {"object":"page","entry":[
              {"id":"PAGE1","time":1731153600,"changes":[
                {"field":"feed","value":{{Comment("add", commentId: "C1")}}},
                {"field":"feed","value":{{Comment("add", commentId: "C2")}}}]},
              {"id":"PAGE2","time":1731153601,"changes":[
                {"field":"feed","value":{{Comment("add", commentId: "C3")}}}]}]}
            """;

        var parsed = Reader().ParseWebhook(Signed(body));

        Assert.True(parsed.IsSuccess, parsed.Error);
        Assert.Equal(new[] { "C1", "C2", "C3" }, parsed.Value!.Select(e => e.PlatformCommentId));
        Assert.Equal(new[] { "PAGE1", "PAGE1", "PAGE2" }, parsed.Value!.Select(e => e.PlatformAccountId));
    }

    [Fact]
    public void An_entry_with_no_page_id_fails_the_delivery()
    {
        var parsed = Reader().ParseWebhook(Signed(
            """{"object":"page","entry":[{"time":1,"changes":[]}]}"""));

        Assert.Equal(SocialFailureCodes.InvalidRequest, parsed.ErrorCode);
    }

    [Fact]
    public void A_signed_notification_for_another_object_is_not_supported_rather_than_dropped()
    {
        var parsed = Reader().ParseWebhook(Signed("""{"object":"instagram","entry":[]}"""));

        Assert.Equal(SocialFailureCodes.NotSupported, parsed.ErrorCode);
        Assert.Contains("instagram", parsed.Error);
    }

    [Fact]
    public void A_signed_body_that_is_not_JSON_is_an_invalid_request()
    {
        Assert.Equal(SocialFailureCodes.InvalidRequest, Reader().ParseWebhook(Signed("not json at all")).ErrorCode);
    }

    // De-duplication keys

    [Fact]
    public void The_same_delivery_parsed_twice_yields_the_same_event_id()
    {
        var body = Feed(Comment("add"));

        Assert.Equal(OneEvent(body).PlatformEventId, OneEvent(body).PlatformEventId);
    }

    [Fact]
    public void A_created_comment_has_one_id_whenever_it_is_redelivered()
    {
        var first = OneEvent(Feed(Comment("add"), entryTime: 1731153600));
        var later = OneEvent(Feed(Comment("add"), entryTime: 1731157200));

        Assert.Equal(first.PlatformEventId, later.PlatformEventId);
    }

    [Fact]
    public void Two_edits_of_one_comment_are_two_events_and_hide_then_unhide_are_two_more()
    {
        var firstEdit = OneEvent(Feed(Comment("edited"), entryTime: 1731153600));
        var secondEdit = OneEvent(Feed(Comment("edited"), entryTime: 1731157200));
        var hide = OneEvent(Feed(Comment("hide"), entryTime: 1731157200));
        var unhide = OneEvent(Feed(Comment("unhide"), entryTime: 1731157200));

        Assert.Equal(4, new[] { firstEdit, secondEdit, hide, unhide }.Select(e => e.PlatformEventId).Distinct().Count());
    }

    [Fact]
    public void An_event_id_never_carries_the_comment_text()
    {
        var id = OneEvent(Feed(Comment("add", message: "my secret question"))).PlatformEventId;

        Assert.DoesNotContain("secret", id);
    }

    // Reading comments for gap recovery

    private static SocialAccount Account() => new() { Platform = SocialPlatforms.Facebook, PlatformAccountId = PageId };

    private const string TokenValue = "EAAB-test-page-token-not-real";

    [Fact]
    public async Task Listing_comments_sends_the_token_in_a_header_and_never_in_the_address()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(request =>
        {
            seen = request;
            return Json("""{"data":[],"paging":{}}""");
        });

        var page = await Reader(handler: handler).ListCommentsAsync(Account(), new SocialToken(TokenValue), "POST1", null);

        Assert.True(page.IsSuccess, page.Error);
        Assert.NotNull(seen);
        Assert.Equal("Bearer", seen!.Headers.Authorization!.Scheme);
        Assert.Equal(TokenValue, seen.Headers.Authorization.Parameter);
        Assert.DoesNotContain(TokenValue, seen.RequestUri!.ToString());
        Assert.StartsWith("https://graph.facebook.test/v27.0/POST1/comments?", seen.RequestUri.ToString());
        Assert.Contains("filter=stream", seen.RequestUri.Query);
    }

    [Fact]
    public async Task Listing_comments_reads_each_comment_and_the_cursor_of_the_next_page()
    {
        var handler = new StubHandler(_ => Json("""
            {"data":[
              {"id":"C1","message":"First","created_time":"2026-11-09T12:00:00+0000","from":{"id":"U1","name":"Test Person"}},
              {"id":"C2","message":"Reply","created_time":"2026-11-09T12:05:00+0000","from":{"id":"U2"},"parent":{"id":"C1"}}],
             "paging":{"cursors":{"before":"B","after":"NEXT"},"next":"https://graph.facebook.test/next"}}
            """));

        var page = await Reader(handler: handler).ListCommentsAsync(Account(), new SocialToken(TokenValue), "POST1", "CUR");

        Assert.True(page.IsSuccess, page.Error);
        Assert.Equal("NEXT", page.Value!.NextCursor);
        Assert.Equal(2, page.Value.Comments.Count);
        var first = page.Value.Comments[0];
        Assert.Equal("C1", first.PlatformCommentId);
        Assert.Equal("First", first.Text);
        Assert.Equal("U1", first.AuthorPlatformId);
        Assert.Equal("Test Person", first.AuthorDisplayName);
        Assert.Null(first.ParentPlatformCommentId);
        Assert.Equal(new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc), first.CreatedAt);
        Assert.Equal("C1", page.Value.Comments[1].ParentPlatformCommentId);
        Assert.Null(page.Value.Comments[1].AuthorDisplayName);
    }

    [Fact]
    public async Task The_last_page_has_no_cursor_even_though_Meta_still_prints_cursors()
    {
        var handler = new StubHandler(_ => Json("""{"data":[],"paging":{"cursors":{"before":"B","after":"A"}}}"""));

        var page = await Reader(handler: handler).ListCommentsAsync(Account(), new SocialToken(TokenValue), "POST1", null);

        Assert.Null(page.Value!.NextCursor);
    }

    [Fact]
    public async Task The_cursor_is_sent_as_after()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(r => { seen = r; return Json("""{"data":[]}"""); });

        await Reader(handler: handler).ListCommentsAsync(Account(), new SocialToken(TokenValue), "POST1", "CUR 1");

        Assert.Contains("after=CUR%201", seen!.RequestUri!.Query);
    }

    [Theory]
    [InlineData(190, 460, SocialFailureCodes.TokenInvalid)]
    [InlineData(4, 0, SocialFailureCodes.RateLimited)]
    [InlineData(613, 0, SocialFailureCodes.RateLimited)]
    [InlineData(200, 0, SocialFailureCodes.Forbidden)]
    [InlineData(10, 0, SocialFailureCodes.Forbidden)]
    [InlineData(100, 33, SocialFailureCodes.NotFound)]
    [InlineData(100, 0, SocialFailureCodes.PlatformRejected)]
    [InlineData(2, 0, SocialFailureCodes.PlatformUnavailable)]
    public async Task A_Graph_error_becomes_the_matching_failure_and_the_message_carries_only_codes(int code, int subcode, string expected)
    {
        var handler = new StubHandler(_ => Json(
            $$$"""{"error":{"message":"Error validating access token for Test Person","type":"OAuthException","code":{{{code}}},"error_subcode":{{{subcode}}},"fbtrace_id":"TRACE1"}}""",
            HttpStatusCode.BadRequest));

        var page = await Reader(handler: handler).ListCommentsAsync(Account(), new SocialToken(TokenValue), "POST1", null);

        Assert.Equal(expected, page.ErrorCode);
        Assert.Contains($"error {code}", page.Error);
        Assert.Contains("TRACE1", page.Error);
        Assert.DoesNotContain("Test Person", page.Error);
        Assert.DoesNotContain(TokenValue, page.Error);
    }

    [Fact]
    public async Task A_server_error_and_an_unreachable_Meta_are_platform_unavailable()
    {
        var down = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") });
        var unreachable = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var viaStatus = await Reader(handler: down).ListCommentsAsync(Account(), new SocialToken(TokenValue), "POST1", null);
        var viaNetwork = await Reader(handler: unreachable).ListCommentsAsync(Account(), new SocialToken(TokenValue), "POST1", null);

        Assert.Equal(SocialFailureCodes.PlatformUnavailable, viaStatus.ErrorCode);
        Assert.Equal(SocialFailureCodes.PlatformUnavailable, viaNetwork.ErrorCode);
    }

    [Fact]
    public async Task A_comment_without_an_id_or_a_readable_time_fails_and_is_not_skipped()
    {
        var noId = new StubHandler(_ => Json("""{"data":[{"message":"x","created_time":"2026-11-09T12:00:00+0000"}]}"""));
        var badTime = new StubHandler(_ => Json("""{"data":[{"id":"C1","message":"x","created_time":"yesterday"}]}"""));

        Assert.Equal(SocialFailureCodes.PlatformRejected,
            (await Reader(handler: noId).ListCommentsAsync(Account(), new SocialToken(TokenValue), "POST1", null)).ErrorCode);
        Assert.Equal(SocialFailureCodes.PlatformRejected,
            (await Reader(handler: badTime).ListCommentsAsync(Account(), new SocialToken(TokenValue), "POST1", null)).ErrorCode);
    }

    [Fact]
    public async Task Listing_comments_refuses_a_blank_post_and_an_account_of_another_platform_before_any_call()
    {
        var reader = Reader();

        Assert.Equal(SocialFailureCodes.InvalidRequest,
            (await reader.ListCommentsAsync(Account(), new SocialToken(TokenValue), " ", null)).ErrorCode);
        Assert.Equal(SocialFailureCodes.InvalidRequest,
            (await reader.ListCommentsAsync(new SocialAccount { Platform = SocialPlatforms.Instagram }, new SocialToken(TokenValue), "POST1", null)).ErrorCode);
    }

    private static SocialHandshakeRequest Handshake(string mode, string token, string challenge) => new()
    {
        Query = new Dictionary<string, string>
        {
            ["hub.mode"] = mode,
            ["hub.verify_token"] = token,
            ["hub.challenge"] = challenge,
        },
    };

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
