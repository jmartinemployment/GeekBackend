using System.Globalization;
using System.Text.Json;
using GeekAPI.Services.Social.Meta;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;

namespace GeekAPI.Services.Social.Connector;

/// <summary>
/// The Connector's Facebook Page half: Facebook Login for Business with a configuration that issues a
/// user access token, traded for a long-lived one, from which each Page the owner chose gives a Page
/// token that does not expire (docs/PLATFORM-CAPABILITIES.md 2a). It checks the grant carries every
/// permission the agent needs, and subscribes each Page's <c>feed</c> to the webhook. The app secret
/// and every token travel in a form body or an Authorization header, never in an address, with one
/// exception Meta imposes: <see cref="GetTokenStatusAsync"/>.
/// </summary>
public sealed class MetaAccountConnector : ISocialAccountConnector
{
    /// <summary>The permissions the login configuration must grant: reading the Pages and their comments, posting, replying and the webhook.</summary>
    public static readonly IReadOnlyList<string> RequiredPermissions = new[]
    {
        "pages_show_list",
        "pages_manage_metadata",
        "pages_read_engagement",
        "pages_read_user_content",
        "pages_manage_posts",
        "pages_manage_engagement",
    };

    /// <summary>The webhook field each Page is subscribed to.</summary>
    public const string FeedField = "feed";

    private const int PagesPerRead = 100;
    private const int MaxPageReads = 10;

    private readonly MetaOptions _options;
    private readonly MetaGraphClient _graph;
    private readonly TimeProvider _clock;

    public MetaAccountConnector(MetaOptions options, MetaGraphClient graph, TimeProvider clock)
    {
        _options = options;
        _graph = graph;
        _clock = clock;
    }

    public string Platform => SocialPlatforms.Facebook;

    public Result<Uri> BuildAuthorizationUrl(string state, Uri redirectUri)
    {
        var missing = Missing(includeLoginConfig: true);
        if (missing is not null)
            return Result<Uri>.Failure(missing, SocialFailureCodes.NotConfigured);
        if (string.IsNullOrWhiteSpace(state))
            return Result<Uri>.Failure("A state value is required.", SocialFailureCodes.InvalidRequest);
        if (!redirectUri.IsAbsoluteUri || redirectUri.Scheme != Uri.UriSchemeHttps && !redirectUri.IsLoopback)
            return Result<Uri>.Failure("The redirect address must be an absolute https address.", SocialFailureCodes.InvalidRequest);

        var query = string.Join('&', new[]
        {
            Pair("client_id", _options.AppId!),
            Pair("redirect_uri", redirectUri.ToString()),
            Pair("state", state),
            Pair("config_id", _options.LoginConfigId!),
            Pair("response_type", "code"),
            Pair("override_default_response_type", "true"),
        });
        return Result<Uri>.Success(new Uri($"https://www.facebook.com/{MetaOptions.GraphVersion}/dialog/oauth?{query}"));
    }

    public async Task<Result<SocialConnection>> ExchangeAuthorizationCodeAsync(string code, Uri redirectUri, CancellationToken ct = default)
    {
        var missing = Missing(includeLoginConfig: false);
        if (missing is not null)
            return Result<SocialConnection>.Failure(missing, SocialFailureCodes.NotConfigured);
        if (string.IsNullOrWhiteSpace(code))
            return Result<SocialConnection>.Failure("The code Meta sent back is missing.", SocialFailureCodes.InvalidRequest);

        var shortLived = await _graph.PostAsAppAsync("oauth/access_token", new[]
        {
            new KeyValuePair<string, string>("client_id", _options.AppId!),
            new("client_secret", _options.AppSecret!),
            new("redirect_uri", redirectUri.ToString()),
            new("code", code),
        }, ct).ConfigureAwait(false);
        if (!shortLived.IsSuccess)
            return Result<SocialConnection>.Failure($"Meta did not accept the login code. {shortLived.Error}", shortLived.ErrorCode!);

        var userToken = StringOf(shortLived.Value, "access_token");
        if (userToken is null)
            return Result<SocialConnection>.Failure("Meta answered the login without a token.", SocialFailureCodes.PlatformRejected);

        var longLived = await _graph.PostAsAppAsync("oauth/access_token", new[]
        {
            new KeyValuePair<string, string>("grant_type", "fb_exchange_token"),
            new("client_id", _options.AppId!),
            new("client_secret", _options.AppSecret!),
            new("fb_exchange_token", userToken),
        }, ct).ConfigureAwait(false);
        if (!longLived.IsSuccess)
            return Result<SocialConnection>.Failure($"Meta did not extend the login. {longLived.Error}", longLived.ErrorCode!);

        var longToken = StringOf(longLived.Value, "access_token");
        if (longToken is null)
            return Result<SocialConnection>.Failure("Meta extended the login without a token.", SocialFailureCodes.PlatformRejected);

        var user = new SocialToken(longToken);
        var granted = await GrantedPermissionsAsync(user, ct).ConfigureAwait(false);
        if (!granted.IsSuccess)
            return Result<SocialConnection>.Failure(granted.Error!, granted.ErrorCode!);

        var lacking = RequiredPermissions.Where(p => !granted.Value!.Contains(p)).ToList();
        if (lacking.Count > 0)
            return Result<SocialConnection>.Failure(
                $"The login did not grant {string.Join(", ", lacking)}. Add them to the Facebook Login for Business configuration and connect again.",
                SocialFailureCodes.Forbidden);

        var connection = new SocialConnection();
        string? after = null;
        for (var read = 0; read < MaxPageReads; read++)
        {
            var query = new List<KeyValuePair<string, string>>
            {
                new("fields", "id,name,access_token,tasks"),
                new("limit", PagesPerRead.ToString(CultureInfo.InvariantCulture)),
            };
            if (after is not null)
                query.Add(new("after", after));

            var pages = await _graph.GetAsync("me/accounts", user, query, ct).ConfigureAwait(false);
            if (!pages.IsSuccess)
                return Result<SocialConnection>.Failure($"Meta did not list the Pages. {pages.Error}", pages.ErrorCode!);

            if (!pages.Value.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return Result<SocialConnection>.Failure("Meta listed the Pages without a data list.", SocialFailureCodes.PlatformRejected);

            foreach (var page in data.EnumerateArray())
            {
                var id = StringOf(page, "id");
                var name = StringOf(page, "name");
                var pageToken = StringOf(page, "access_token");
                if (id is null || name is null || pageToken is null)
                    return Result<SocialConnection>.Failure(
                        "Meta listed a Page without its id, name or token. Check the person has a task on that Page.", SocialFailureCodes.PlatformRejected);

                // A Page token made from a long-lived user token has no expiry (matrix 2a, Tokens).
                connection.Accounts.Add(new SocialConnectedAccount
                {
                    Platform = SocialPlatforms.Facebook,
                    PlatformAccountId = id,
                    DisplayName = name,
                    Token = new SocialToken(pageToken),
                });
            }

            after = pages.Value.TryGetProperty("paging", out var paging)
                && paging.TryGetProperty("next", out _)
                && paging.TryGetProperty("cursors", out var cursors)
                    ? StringOf(cursors, "after")
                    : null;
            if (after is null)
                break;
            if (read == MaxPageReads - 1)
                return Result<SocialConnection>.Failure(
                    $"The login granted more than {PagesPerRead * MaxPageReads} Pages. Choose fewer on the consent screen.", SocialFailureCodes.InvalidRequest);
        }

        if (connection.Accounts.Count == 0)
            return Result<SocialConnection>.Failure(
                "The login granted no Page. Choose at least one Page on Meta's consent screen.", SocialFailureCodes.InvalidRequest);

        return Result<SocialConnection>.Success(connection);
    }

    /// <summary>The permissions the login holds now, read with the token in the Authorization header.</summary>
    private async Task<Result<HashSet<string>>> GrantedPermissionsAsync(SocialToken user, CancellationToken ct)
    {
        var answer = await _graph.GetAsync("me/permissions", user, Array.Empty<KeyValuePair<string, string>>(), ct).ConfigureAwait(false);
        if (!answer.IsSuccess)
            return Result<HashSet<string>>.Failure($"Meta did not list the login's permissions. {answer.Error}", answer.ErrorCode!);

        if (!answer.Value.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Result<HashSet<string>>.Failure("Meta listed the permissions without a data list.", SocialFailureCodes.PlatformRejected);

        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            if (StringOf(item, "status") == "granted" && StringOf(item, "permission") is { } permission)
                granted.Add(permission);
        }

        return Result<HashSet<string>>.Success(granted);
    }

    /// <summary>A Page token made from a long-lived user token has no expiry and no renewal; there is nothing to renew.</summary>
    public Task<Result<SocialToken>> RefreshTokenAsync(SocialToken token, CancellationToken ct = default) =>
        Task.FromResult(Result<SocialToken>.Failure(
            "A Facebook Page token does not expire and has no renewal; reconnect the Page if Meta refuses it.", SocialFailureCodes.NotSupported));

    /// <summary>
    /// Meta's <c>debug_token</c>. It is the one call that takes the token being checked in the address,
    /// because Meta accepts it nowhere else; <see cref="MetaGraphClient"/> keeps no request log, so the
    /// address is never written down. The connect flow does not use it: it reads <c>me/permissions</c>.
    /// </summary>
    public async Task<Result<SocialTokenStatus>> GetTokenStatusAsync(SocialToken token, CancellationToken ct = default)
    {
        var missing = Missing(includeLoginConfig: false);
        if (missing is not null)
            return Result<SocialTokenStatus>.Failure(missing, SocialFailureCodes.NotConfigured);

        var answer = await _graph.GetWithAppTokenAsync(
            "debug_token",
            $"{_options.AppId}|{_options.AppSecret}",
            new[] { new KeyValuePair<string, string>("input_token", token.Reveal()) },
            ct).ConfigureAwait(false);
        if (!answer.IsSuccess)
            return Result<SocialTokenStatus>.Failure($"Meta did not check the token. {answer.Error}", answer.ErrorCode!);

        if (!answer.Value.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return Result<SocialTokenStatus>.Failure("Meta checked the token without saying anything about it.", SocialFailureCodes.PlatformRejected);

        var valid = data.TryGetProperty("is_valid", out var isValid) && isValid.ValueKind == JsonValueKind.True;
        if (!valid)
            return Result<SocialTokenStatus>.Failure("Meta says the token is not valid.", SocialFailureCodes.TokenInvalid);

        if (StringOf(data, "app_id") is { } appId && appId != _options.AppId)
            return Result<SocialTokenStatus>.Failure("The token belongs to another Meta app.", SocialFailureCodes.Forbidden);

        DateTime? expiresAt = null;
        if (data.TryGetProperty("expires_at", out var expires) && expires.ValueKind == JsonValueKind.Number
            && expires.TryGetInt64(out var seconds) && seconds > 0)
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

        var scopes = new List<string>();
        if (data.TryGetProperty("scopes", out var granted) && granted.ValueKind == JsonValueKind.Array)
            scopes.AddRange(granted.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!));

        if (expiresAt is { } at && at <= _clock.GetUtcNow().UtcDateTime)
            return Result<SocialTokenStatus>.Failure("The token has expired.", SocialFailureCodes.TokenInvalid);

        return Result<SocialTokenStatus>.Success(new SocialTokenStatus { IsValid = true, ExpiresAt = expiresAt, GrantedScopes = scopes });
    }

    public async Task<Result<SocialWebhookSubscription>> EnsureWebhookSubscriptionAsync(
        SocialAccount account, SocialToken token, Uri callbackUrl, CancellationToken ct = default)
    {
        if (account.Platform != SocialPlatforms.Facebook)
            return Result<SocialWebhookSubscription>.Failure("This connector serves Facebook Pages only.", SocialFailureCodes.InvalidRequest);

        // The callback address itself is set once in the Meta app's dashboard, with the verify token;
        // a Page subscription names only the fields. callbackUrl is checked so a wrong deployment is seen.
        if (!callbackUrl.IsAbsoluteUri || callbackUrl.Scheme != Uri.UriSchemeHttps)
            return Result<SocialWebhookSubscription>.Failure("The webhook address must be an absolute https address.", SocialFailureCodes.InvalidRequest);

        var path = $"{Uri.EscapeDataString(account.PlatformAccountId)}/subscribed_apps";
        var subscribed = await _graph.PostFormAsync(path, token,
            new[] { new KeyValuePair<string, string>("subscribed_fields", FeedField) }, ct).ConfigureAwait(false);
        if (!subscribed.IsSuccess)
            return Result<SocialWebhookSubscription>.Failure($"Meta did not subscribe the Page. {subscribed.Error}", subscribed.ErrorCode!);
        if (!(subscribed.Value.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True))
            return Result<SocialWebhookSubscription>.Failure("Meta answered the subscription without confirming it.", SocialFailureCodes.PlatformRejected);

        // Read it back: success means Meta took the request, the listing says what it holds.
        var listed = await _graph.GetAsync(path, token, Array.Empty<KeyValuePair<string, string>>(), ct).ConfigureAwait(false);
        if (!listed.IsSuccess)
            return Result<SocialWebhookSubscription>.Failure($"Meta did not list the Page's subscriptions. {listed.Error}", listed.ErrorCode!);

        var fields = new List<string>();
        if (listed.Value.TryGetProperty("data", out var apps) && apps.ValueKind == JsonValueKind.Array)
        {
            foreach (var app in apps.EnumerateArray())
            {
                if (StringOf(app, "id") != _options.AppId)
                    continue;
                if (app.TryGetProperty("subscribed_fields", out var list) && list.ValueKind == JsonValueKind.Array)
                    fields.AddRange(list.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.String).Select(f => f.GetString()!));
            }
        }

        if (!fields.Contains(FeedField))
            return Result<SocialWebhookSubscription>.Failure(
                "Meta confirmed the subscription and does not list the feed field for this app.", SocialFailureCodes.PlatformRejected);

        return Result<SocialWebhookSubscription>.Success(new SocialWebhookSubscription { Active = true, Fields = fields });
    }

    private string? Missing(bool includeLoginConfig)
    {
        var names = new List<string>();
        if (_options.AppId is null) names.Add(MetaOptions.AppIdVariable);
        if (_options.AppSecret is null) names.Add(MetaOptions.AppSecretVariable);
        if (includeLoginConfig && _options.LoginConfigId is null) names.Add(MetaOptions.LoginConfigIdVariable);
        return names.Count == 0 ? null : $"{string.Join(", ", names)} {(names.Count == 1 ? "is" : "are")} not set on GeekAPI, so a Page cannot be connected.";
    }

    private static string Pair(string key, string value) => $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";

    private static string? StringOf(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
