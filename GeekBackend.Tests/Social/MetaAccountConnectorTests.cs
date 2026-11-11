using System.Net;
using GeekAPI.Services.Social.Connector;
using GeekAPI.Services.Social.Meta;
using GeekApplication.Models.Social;

namespace GeekBackend.Tests.Social;

/// <summary>
/// Connecting a Facebook Page: the consent address names the login configuration; the code becomes a
/// long-lived login whose permissions are checked before any Page is taken; each Page's token is kept;
/// the app secret and every token travel in a form body or a header, never an address; and a webhook
/// subscription counts only when Meta lists the feed field for this app.
/// </summary>
public class MetaAccountConnectorTests
{
    private const string AppId = "APP1";
    private const string AppSecret = "PRIVATE-APP-SECRET";
    private const string ConfigId = "CONFIG1";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly Uri Redirect = new("https://social.geekatyourspot.com/connect/meta/callback");

    private static MetaOptions Options(bool configured = true) => MetaOptions.From(name => !configured ? null : name switch
    {
        MetaOptions.AppIdVariable => AppId,
        MetaOptions.AppSecretVariable => AppSecret,
        MetaOptions.LoginConfigIdVariable => ConfigId,
        MetaOptions.VerifyTokenVariable => "verify",
        _ => null,
    });

    private sealed class Recorder : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Address, string? Body, string? Auth)> Calls { get; } = new();
        public required Func<HttpRequestMessage, string?, HttpResponseMessage> Respond { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
            return Respond(request, body);
        }
    }

    private static string Scopes(params string[] scopes) =>
        string.Join(",", scopes.Select(s => $"{{\"permission\":\"{s}\",\"status\":\"granted\"}}"));

    private static Recorder Meta(string? scopes = null, string pages = """{"data":[{"id":"PAGE1","name":"Geek @ Your Spot","access_token":"PRIVATE-PAGE-TOKEN","tasks":["MODERATE"]}]}""") => new()
    {
        Respond = (request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/oauth/access_token", StringComparison.Ordinal))
                return body!.Contains("fb_exchange_token=", StringComparison.Ordinal)
                    ? ScriptedModelHandler.Json(HttpStatusCode.OK, """{"access_token":"PRIVATE-LONG-TOKEN","expires_in":5184000}""")
                    : ScriptedModelHandler.Json(HttpStatusCode.OK, """{"access_token":"PRIVATE-SHORT-TOKEN","expires_in":3600}""");
            if (path.EndsWith("/me/permissions", StringComparison.Ordinal))
                return ScriptedModelHandler.Json(HttpStatusCode.OK,
                    $$$"""{"data":[{{{scopes ?? Scopes(MetaAccountConnector.RequiredPermissions.ToArray())}}},{"permission":"public_profile","status":"declined"}]}""");
            if (path.EndsWith("/me/accounts", StringComparison.Ordinal))
                return ScriptedModelHandler.Json(HttpStatusCode.OK, pages);
            throw new InvalidOperationException($"Unexpected call to {path}.");
        },
    };

    private static MetaAccountConnector Connector(HttpMessageHandler handler, MetaOptions? options = null) =>
        new(options ?? Options(), new MetaGraphClient(new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.test/") }),
            new MetaEngagementReaderTests.FixedClock(Now));

    [Fact]
    public void The_consent_address_names_the_app_the_login_configuration_the_state_and_the_return_address()
    {
        var url = Connector(new Recorder { Respond = (_, _) => throw new InvalidOperationException() }).BuildAuthorizationUrl("STATE1", Redirect);

        Assert.True(url.IsSuccess, url.Error);
        var address = url.Value!.ToString();
        Assert.StartsWith($"https://www.facebook.com/{MetaOptions.GraphVersion}/dialog/oauth?", address, StringComparison.Ordinal);
        Assert.Contains($"client_id={AppId}", address, StringComparison.Ordinal);
        Assert.Contains($"config_id={ConfigId}", address, StringComparison.Ordinal);
        Assert.Contains("state=STATE1", address, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=https%3A%2F%2Fsocial.geekatyourspot.com%2Fconnect%2Fmeta%2Fcallback", address, StringComparison.Ordinal);
        Assert.Contains("response_type=code", address, StringComparison.Ordinal);
        Assert.DoesNotContain(AppSecret, address, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_app_settings_nothing_can_be_connected_and_the_variables_are_named()
    {
        var url = Connector(new Recorder { Respond = (_, _) => throw new InvalidOperationException() }, Options(configured: false))
            .BuildAuthorizationUrl("STATE1", Redirect);

        Assert.Equal(SocialFailureCodes.NotConfigured, url.ErrorCode);
        Assert.Contains(MetaOptions.AppIdVariable, url.Error, StringComparison.Ordinal);
        Assert.Contains(MetaOptions.LoginConfigIdVariable, url.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_code_becomes_each_granted_page_with_its_own_token_and_no_secret_or_token_in_any_address()
    {
        var meta = Meta();

        var connection = await Connector(meta).ExchangeAuthorizationCodeAsync("CODE1", Redirect);

        Assert.True(connection.IsSuccess, connection.Error);
        var page = Assert.Single(connection.Value!.Accounts);
        Assert.Equal("PAGE1", page.PlatformAccountId);
        Assert.Equal("Geek @ Your Spot", page.DisplayName);
        Assert.Equal("PRIVATE-PAGE-TOKEN", page.Token.Reveal());
        Assert.Null(page.Token.ExpiresAt);

        Assert.Equal(4, meta.Calls.Count);
        Assert.All(meta.Calls, call =>
        {
            Assert.DoesNotContain("PRIVATE", call.Address, StringComparison.Ordinal);
            Assert.DoesNotContain(AppSecret, call.Address, StringComparison.Ordinal);
        });
        Assert.Contains("code=CODE1", meta.Calls[0].Body, StringComparison.Ordinal);
        Assert.Contains("client_secret=PRIVATE-APP-SECRET", meta.Calls[0].Body, StringComparison.Ordinal);
        Assert.Contains("fb_exchange_token=PRIVATE-SHORT-TOKEN", meta.Calls[1].Body, StringComparison.Ordinal);
        Assert.Equal("Bearer PRIVATE-LONG-TOKEN", meta.Calls[2].Auth);
        Assert.Equal("Bearer PRIVATE-LONG-TOKEN", meta.Calls[3].Auth);
    }

    [Fact]
    public async Task A_login_missing_a_permission_is_refused_and_names_what_is_missing()
    {
        var granted = MetaAccountConnector.RequiredPermissions.Where(p => p != "pages_manage_engagement").ToArray();

        var connection = await Connector(Meta(scopes: Scopes(granted))).ExchangeAuthorizationCodeAsync("CODE1", Redirect);

        Assert.Equal(SocialFailureCodes.Forbidden, connection.ErrorCode);
        Assert.Contains("pages_manage_engagement", connection.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_granted_no_page_is_refused()
    {
        var connection = await Connector(Meta(pages: """{"data":[]}""")).ExchangeAuthorizationCodeAsync("CODE1", Redirect);

        Assert.Equal(SocialFailureCodes.InvalidRequest, connection.ErrorCode);
    }

    [Fact]
    public async Task A_page_listed_without_its_token_is_refused_and_not_skipped()
    {
        var connection = await Connector(Meta(pages: """{"data":[{"id":"PAGE1","name":"Geek @ Your Spot"}]}"""))
            .ExchangeAuthorizationCodeAsync("CODE1", Redirect);

        Assert.Equal(SocialFailureCodes.PlatformRejected, connection.ErrorCode);
    }

    [Fact]
    public async Task A_code_meta_refuses_fails_with_its_code_and_without_meta_s_words()
    {
        var meta = new Recorder
        {
            Respond = (_, _) => ScriptedModelHandler.Json(HttpStatusCode.BadRequest,
                """{"error":{"code":100,"message":"PRIVATE code has expired","fbtrace_id":"T1"}}"""),
        };

        var connection = await Connector(meta).ExchangeAuthorizationCodeAsync("CODE1", Redirect);

        Assert.Equal(SocialFailureCodes.PlatformRejected, connection.ErrorCode);
        Assert.DoesNotContain("PRIVATE", connection.Error, StringComparison.Ordinal);
        Assert.Single(meta.Calls);
    }

    [Fact]
    public async Task A_page_token_has_no_renewal()
    {
        var renewed = await Connector(new Recorder { Respond = (_, _) => throw new InvalidOperationException() })
            .RefreshTokenAsync(new SocialToken("t"));

        Assert.Equal(SocialFailureCodes.NotSupported, renewed.ErrorCode);
    }

    [Fact]
    public async Task A_token_meta_calls_invalid_is_token_invalid()
    {
        var meta = new Recorder { Respond = (_, _) => ScriptedModelHandler.Json(HttpStatusCode.OK, """{"data":{"is_valid":false}}""") };

        var status = await Connector(meta).GetTokenStatusAsync(new SocialToken("t"));

        Assert.Equal(SocialFailureCodes.TokenInvalid, status.ErrorCode);
        Assert.Equal($"Bearer {AppId}|{AppSecret}", Assert.Single(meta.Calls).Auth);
        Assert.DoesNotContain(AppSecret, meta.Calls[0].Address, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_webhook_counts_as_subscribed_only_when_meta_lists_the_feed_field_for_this_app()
    {
        var page = new SocialAccount { Id = Guid.NewGuid(), Platform = SocialPlatforms.Facebook, PlatformAccountId = "PAGE1" };
        Recorder Subscriptions(string listed) => new()
        {
            Respond = (request, _) => request.Method == HttpMethod.Post
                ? ScriptedModelHandler.Json(HttpStatusCode.OK, """{"success":true}""")
                : ScriptedModelHandler.Json(HttpStatusCode.OK, listed),
        };
        var webhook = new Uri("https://api.geekatyourspot.com/api/social/webhooks/meta");

        var good = Subscriptions($$$"""{"data":[{"id":"{{{AppId}}}","subscribed_fields":["feed"]}]}""");
        var subscribed = await Connector(good).EnsureWebhookSubscriptionAsync(page, new SocialToken("PRIVATE-PAGE-TOKEN"), webhook);
        var missing = await Connector(Subscriptions("""{"data":[{"id":"OTHER-APP","subscribed_fields":["feed"]}]}"""))
            .EnsureWebhookSubscriptionAsync(page, new SocialToken("PRIVATE-PAGE-TOKEN"), webhook);

        Assert.True(subscribed.IsSuccess, subscribed.Error);
        Assert.Contains("feed", subscribed.Value!.Fields);
        Assert.Contains("subscribed_fields=feed", good.Calls[0].Body, StringComparison.Ordinal);
        Assert.EndsWith("/PAGE1/subscribed_apps", new Uri(good.Calls[0].Address).AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(SocialFailureCodes.PlatformRejected, missing.ErrorCode);
    }
}
