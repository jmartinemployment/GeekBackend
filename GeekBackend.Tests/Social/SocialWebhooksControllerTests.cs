using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.Controllers.Social;
using GeekAPI.Middleware;
using GeekAPI.Services.Social.Meta;
using GeekAPI.Services.Social.Orchestration;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The public webhook endpoint. It answers 200 only once every event in a delivery is stored, so a
/// platform that is told anything else redelivers, and the store's one-row-per-event rule makes the
/// redelivery harmless.
/// </summary>
public class SocialWebhooksControllerTests
{
    private const string Secret = "test-app-secret-not-real";
    private const string VerifyToken = "test-verify-token-not-real";

    private static readonly string CommentDelivery =
        """{"object":"page","entry":[{"id":"PAGE1","time":1731153600,"changes":[{"field":"feed","value":{"from":{"id":"U1","name":"Test Person"},"item":"comment","comment_id":"C1","post_id":"POST1","verb":"add","parent_id":"POST1","created_time":1731153590,"message":"Café hours?"}}]}]}""";

    private sealed class Harness
    {
        public required SocialWebhooksController Controller { get; init; }
        public required FakeSocialStore Store { get; init; }
        public required SocialWake Wake { get; init; }
    }

    private static Harness Build(MetaOptions? options = null, bool registerReader = true)
    {
        var store = new FakeSocialStore { OnRecordEvent = _ => Result<bool>.Success(true) };
        var wake = new SocialWake();
        var services = new ServiceCollection();
        if (registerReader)
        {
            var reader = new MetaEngagementReader(
                options ?? new MetaOptions { AppSecret = Secret, VerifyToken = VerifyToken },
                new MetaGraphClient(new HttpClient(new MetaEngagementReaderTests.StubHandler(_ => throw new InvalidOperationException("No call expected.")))
                {
                    BaseAddress = new Uri("https://graph.facebook.test/"),
                }),
                new MetaEngagementReaderTests.FixedClock(new DateTimeOffset(2026, 11, 9, 18, 0, 0, TimeSpan.Zero)));
            services.AddKeyedSingleton<ISocialEngagementReader>(SocialPlatforms.Facebook, (_, _) => reader);
        }

        var controller = new SocialWebhooksController(services.BuildServiceProvider(), store, wake, NullLogger<SocialWebhooksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return new Harness { Controller = controller, Store = store, Wake = wake };
    }

    private static void Post(Harness h, byte[] body, string? signature)
    {
        var request = h.Controller.HttpContext.Request;
        request.Method = "POST";
        request.Body = new MemoryStream(body);
        request.ContentLength = body.Length;
        if (signature is not null)
            request.Headers["X-Hub-Signature-256"] = signature;
    }

    private static string Sign(byte[] body, string secret = Secret) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    private static void Query(Harness h, string mode, string token, string challenge) =>
        h.Controller.HttpContext.Request.QueryString =
            new QueryString($"?hub.mode={mode}&hub.verify_token={Uri.EscapeDataString(token)}&hub.challenge={Uri.EscapeDataString(challenge)}");

    private static int StatusOf(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        ContentResult c => c.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => throw new InvalidOperationException($"Unexpected result {result.GetType().Name}."),
    };

    private static JsonElement BodyOf(IActionResult result) =>
        JsonSerializer.SerializeToElement(((ObjectResult)result).Value);

    private static List<Guid> Woken(SocialWake wake)
    {
        var ids = new List<Guid>();
        while (wake.Reader.TryRead(out var id))
            ids.Add(id);
        return ids;
    }

    // The handshake

    [Fact]
    public void The_handshake_answers_200_with_the_challenge_as_plain_text()
    {
        var h = Build();
        Query(h, "subscribe", VerifyToken, "1158201444");

        var result = Assert.IsType<ContentResult>(h.Controller.MetaHandshake());

        Assert.Equal("1158201444", result.Content);
        Assert.Equal("text/plain", result.ContentType);
    }

    [Fact]
    public void A_handshake_with_the_wrong_token_is_403_and_one_with_no_token_configured_is_503()
    {
        var wrong = Build();
        Query(wrong, "subscribe", "not-it", "1");
        var unset = Build(new MetaOptions { AppSecret = Secret });
        Query(unset, "subscribe", "anything", "1");

        Assert.Equal(403, StatusOf(wrong.Controller.MetaHandshake()));
        var notConfigured = unset.Controller.MetaHandshake();
        Assert.Equal(503, StatusOf(notConfigured));
        Assert.Equal(SocialFailureCodes.NotConfigured, BodyOf(notConfigured).GetProperty("code").GetString());
    }

    [Fact]
    public void The_handshake_never_touches_the_store()
    {
        // The fake store throws on any method the test did not script, and the handshake scripts none.
        var h = Build();
        h.Store.OnRecordEvent = null;
        Query(h, "subscribe", VerifyToken, "1");

        Assert.Equal(200, StatusOf(h.Controller.MetaHandshake()));
    }

    // A delivery

    [Fact]
    public async Task A_signed_comment_is_stored_once_and_wakes_the_orchestrator_with_its_id()
    {
        var h = Build();
        var body = Encoding.UTF8.GetBytes(CommentDelivery);
        Post(h, body, Sign(body));

        var result = await h.Controller.MetaDelivery(CancellationToken.None);

        Assert.Equal(200, StatusOf(result));
        var stored = Assert.Single(h.Store.RecordedEvents);
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(EngagementKinds.CommentCreated, stored.Kind);
        Assert.Equal("PAGE1", stored.PlatformAccountId);
        Assert.Equal(new[] { stored.Id }, Woken(h.Wake));
        var answer = BodyOf(result);
        Assert.Equal((1, 1, 0), (answer.GetProperty("received").GetInt32(), answer.GetProperty("stored").GetInt32(), answer.GetProperty("repeated").GetInt32()));
    }

    [Fact]
    public async Task A_redelivery_is_answered_200_and_wakes_nothing()
    {
        var h = Build();
        h.Store.OnRecordEvent = _ => Result<bool>.Success(false);
        var body = Encoding.UTF8.GetBytes(CommentDelivery);
        Post(h, body, Sign(body));

        var result = await h.Controller.MetaDelivery(CancellationToken.None);

        Assert.Equal(200, StatusOf(result));
        Assert.Empty(Woken(h.Wake));
        Assert.Equal(1, BodyOf(result).GetProperty("repeated").GetInt32());
    }

    [Fact]
    public async Task The_signature_is_checked_against_the_bytes_received_including_non_ASCII_text()
    {
        var h = Build();
        var body = Encoding.UTF8.GetBytes(CommentDelivery);
        Assert.Contains(body, b => b > 127);
        Post(h, body, Sign(body));

        Assert.Equal(200, StatusOf(await h.Controller.MetaDelivery(CancellationToken.None)));
        Assert.Equal("Café hours?", h.Store.RecordedEvents[0].Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256=00")]
    [InlineData("not a signature")]
    public async Task A_delivery_with_a_bad_or_missing_signature_is_401_and_stores_nothing(string? header)
    {
        var h = Build();
        Post(h, Encoding.UTF8.GetBytes(CommentDelivery), header);

        var result = await h.Controller.MetaDelivery(CancellationToken.None);

        Assert.Equal(401, StatusOf(result));
        Assert.Empty(h.Store.RecordedEvents);
        Assert.Empty(Woken(h.Wake));
    }

    [Fact]
    public async Task A_delivery_signed_with_another_secret_is_401()
    {
        var h = Build();
        var body = Encoding.UTF8.GetBytes(CommentDelivery);
        Post(h, body, Sign(body, "someone-elses-secret"));

        Assert.Equal(401, StatusOf(await h.Controller.MetaDelivery(CancellationToken.None)));
        Assert.Empty(h.Store.RecordedEvents);
    }

    [Fact]
    public async Task With_no_app_secret_a_delivery_is_503_so_it_is_never_accepted_unverified()
    {
        var h = Build(new MetaOptions { VerifyToken = VerifyToken });
        var body = Encoding.UTF8.GetBytes(CommentDelivery);
        Post(h, body, Sign(body));

        var result = await h.Controller.MetaDelivery(CancellationToken.None);

        Assert.Equal(503, StatusOf(result));
        Assert.Equal(SocialFailureCodes.NotConfigured, BodyOf(result).GetProperty("code").GetString());
        Assert.Empty(h.Store.RecordedEvents);
    }

    [Fact]
    public async Task A_store_that_cannot_take_the_event_is_503_so_Meta_redelivers_and_nothing_is_woken()
    {
        var h = Build();
        h.Store.OnRecordEvent = _ => Result<bool>.Failure("down", SocialFailureCodes.StoreUnavailable);
        var body = Encoding.UTF8.GetBytes(CommentDelivery);
        Post(h, body, Sign(body));

        Assert.Equal(503, StatusOf(await h.Controller.MetaDelivery(CancellationToken.None)));
        Assert.Empty(Woken(h.Wake));
    }

    [Fact]
    public async Task An_event_for_a_Page_that_is_not_connected_is_404_and_not_silently_dropped()
    {
        var h = Build();
        h.Store.OnRecordEvent = _ => Result<bool>.Failure(
            "No connected facebook account matches the event's account.", SocialFailureCodes.NotFound);
        var body = Encoding.UTF8.GetBytes(CommentDelivery);
        Post(h, body, Sign(body));

        Assert.Equal(404, StatusOf(await h.Controller.MetaDelivery(CancellationToken.None)));
        Assert.Empty(Woken(h.Wake));
    }

    [Fact]
    public async Task When_a_later_event_fails_the_earlier_ones_stay_stored_and_woken_and_the_delivery_is_not_acknowledged()
    {
        var h = Build();
        var calls = 0;
        h.Store.OnRecordEvent = _ => ++calls == 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure("down", SocialFailureCodes.StoreUnavailable);
        var body = Encoding.UTF8.GetBytes("""
            {"object":"page","entry":[{"id":"PAGE1","time":1731153600,"changes":[
              {"field":"feed","value":{"item":"comment","comment_id":"C1","post_id":"POST1","verb":"add","parent_id":"POST1","created_time":1731153590}},
              {"field":"feed","value":{"item":"comment","comment_id":"C2","post_id":"POST1","verb":"add","parent_id":"POST1","created_time":1731153591}}]}]}
            """);
        Post(h, body, Sign(body));

        var status = StatusOf(await h.Controller.MetaDelivery(CancellationToken.None));

        Assert.Equal(503, status);
        Assert.Equal(2, h.Store.RecordedEvents.Count);
        Assert.Equal(new[] { h.Store.RecordedEvents[0].Id }, Woken(h.Wake));
    }

    [Fact]
    public async Task A_delivery_of_only_non_engagement_changes_is_acknowledged_with_nothing_stored()
    {
        var h = Build();
        var body = Encoding.UTF8.GetBytes(
            """{"object":"page","entry":[{"id":"PAGE1","time":1,"changes":[{"field":"feed","value":{"item":"status","verb":"add","created_time":1}}]}]}""");
        Post(h, body, Sign(body));

        var result = await h.Controller.MetaDelivery(CancellationToken.None);

        Assert.Equal(200, StatusOf(result));
        Assert.Empty(h.Store.RecordedEvents);
        Assert.Equal(0, BodyOf(result).GetProperty("received").GetInt32());
    }

    [Fact]
    public async Task A_delivery_for_an_object_not_handled_is_422_and_one_that_is_not_JSON_is_400()
    {
        var instagram = Build();
        var instagramBody = Encoding.UTF8.GetBytes("""{"object":"instagram","entry":[]}""");
        Post(instagram, instagramBody, Sign(instagramBody));
        var garbage = Build();
        var garbageBody = Encoding.UTF8.GetBytes("not json");
        Post(garbage, garbageBody, Sign(garbageBody));

        Assert.Equal(422, StatusOf(await instagram.Controller.MetaDelivery(CancellationToken.None)));
        Assert.Equal(400, StatusOf(await garbage.Controller.MetaDelivery(CancellationToken.None)));
    }

    [Fact]
    public async Task A_delivery_larger_than_the_limit_is_413_whether_or_not_it_declares_its_length()
    {
        var declared = Build();
        Post(declared, new byte[10], null);
        declared.Controller.HttpContext.Request.ContentLength = SocialWebhooksController.MaxBodyBytes + 1;

        var undeclared = Build();
        var big = new byte[SocialWebhooksController.MaxBodyBytes + 1];
        undeclared.Controller.HttpContext.Request.Body = new MemoryStream(big);

        Assert.Equal(413, StatusOf(await declared.Controller.MetaDelivery(CancellationToken.None)));
        Assert.Equal(413, StatusOf(await undeclared.Controller.MetaDelivery(CancellationToken.None)));
    }

    [Fact]
    public async Task With_no_reader_registered_both_routes_are_503()
    {
        var h = Build(registerReader: false);
        Post(h, Encoding.UTF8.GetBytes(CommentDelivery), null);

        Assert.Equal(503, StatusOf(h.Controller.MetaHandshake()));
        Assert.Equal(503, StatusOf(await h.Controller.MetaDelivery(CancellationToken.None)));
    }

    [Theory]
    [InlineData(SocialFailureCodes.InvalidSignature, 401)]
    [InlineData(SocialFailureCodes.Forbidden, 403)]
    [InlineData(SocialFailureCodes.InvalidRequest, 400)]
    [InlineData(SocialFailureCodes.NotSupported, 422)]
    [InlineData(SocialFailureCodes.NotFound, 404)]
    [InlineData(SocialFailureCodes.Conflict, 409)]
    [InlineData(SocialFailureCodes.NotConfigured, 503)]
    [InlineData(SocialFailureCodes.StoreUnavailable, 503)]
    [InlineData(SocialFailureCodes.EncryptionUnavailable, 503)]
    [InlineData(null, 503)]
    public void Each_failure_code_has_one_status_and_the_unlisted_ones_make_the_platform_retry(string? code, int status) =>
        Assert.Equal(status, SocialWebhooksController.StatusFor(code));

    // The route and the key check

    [Fact]
    public void The_endpoint_is_exactly_api_social_webhooks_meta_for_GET_and_POST()
    {
        var route = typeof(SocialWebhooksController).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Single();
        var get = typeof(SocialWebhooksController).GetMethod(nameof(SocialWebhooksController.MetaHandshake))!.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>().Single();
        var post = typeof(SocialWebhooksController).GetMethod(nameof(SocialWebhooksController.MetaDelivery))!.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>().Single();

        Assert.Equal("api/social/webhooks", route.Template);
        Assert.Equal("meta", get.Template);
        Assert.Equal("meta", post.Template);
    }

    [Theory]
    [InlineData("/api/social/webhooks/meta", true)]
    [InlineData("/api/social/webhooks/meta/", true)]
    [InlineData("/API/SOCIAL/WEBHOOKS/META", true)]
    [InlineData("/api/social/webhooks", false)]
    [InlineData("/api/social/webhooks/meta/extra", false)]
    [InlineData("/api/social/webhooks/linkedin", false)]
    [InlineData("/api/social/accounts", false)]
    [InlineData("/api/social/internal/accounts", false)]
    public async Task Only_the_Meta_webhook_path_skips_the_API_key_and_every_other_social_path_still_needs_it(string path, bool open)
    {
        var reached = false;
        var middleware = new ApiKeyMiddleware(_ => { reached = true; return Task.CompletedTask; }, new TestEnvironment("Production"));
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(open, reached);
        if (!open)
            Assert.Equal(401, context.Response.StatusCode);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
