using System.Net;
using System.Text.Json;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>What Google handed back for an approved connection. The refresh token is what keeps the connection alive after the access token lapses.</summary>
public sealed record GoogleCalendarTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>
/// A renewed access token, when it lapses, and the refresh token to keep. <paramref name="RefreshToken"/> is null when the vendor did
/// not send a new one, which means the stored one stays valid: Google works that way. Microsoft does not - it "replace[s] themselves
/// with a fresh token upon every use" - so for a rotating vendor this carries the token that must be stored in its place.
/// </summary>
public sealed record GoogleCalendarAccess(string AccessToken, DateTimeOffset ExpiresAt, string? RefreshToken = null);

/// <summary>
/// Google's side of connecting a tenant's calendar: the address the person is sent to, and the one-time code exchange. Google answering
/// invalid_grant means the code was used, expired or never issued for this client, which is the visitor's mistake (400). A refusal of
/// our own client, a 5xx, a timeout and an answer that is not what Google documents are all connection unavailable (503), logged with
/// the status and never with a body, a code or a token. The exchange is made once and never retried: the code works once.
/// </summary>
public sealed class GoogleCalendarOAuth(HttpClient http, GoogleCalendarOptions options, TimeProvider clock, ILogger<GoogleCalendarOAuth> logger)
{
    /// <summary>
    /// The address that asks the person to approve. access_type=offline and prompt=consent make Google return a refresh token every
    /// time; without them a second connection by the same person comes back with none and cannot be kept alive.
    /// </summary>
    public string AuthorizationUrl(string state) => AuthorizationUrl(state, McpProviders.Calendar);

    public string AuthorizationUrl(string state, McpProviderOAuth connection)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId!,
            ["redirect_uri"] = options.RedirectUriFor(connection.Provider),
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', connection.Scopes),
            ["state"] = state,
        };
        foreach (var (name, value) in connection.ExtraAuthorizationParameters) query[name] = value;
        var encoded = string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return $"{connection.AuthorizeEndpoint}?{encoded}";
    }

    public Task<Result<GoogleCalendarTokens>> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
        ExchangeCodeAsync(code, McpProviders.Calendar, ct);

    public async Task<Result<GoogleCalendarTokens>> ExchangeCodeAsync(string code, McpProviderOAuth connection, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, connection.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
                ["redirect_uri"] = options.RedirectUriFor(connection.Provider),
                ["grant_type"] = "authorization_code",
            }),
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Google's token endpoint is unreachable.");
            return Unavailable("Google could not be reached.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Google's token endpoint timed out.");
            return Unavailable("Google did not answer in time.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.BadRequest && ErrorOf(body) == "invalid_grant")
            {
                logger.LogWarning("Google refused a calendar authorization code: invalid_grant.");
                return Result<GoogleCalendarTokens>.Failure("That Google approval is no longer valid. Start the connection again.", McpFailureCodes.CodeExchangeFailed);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Google's token endpoint answered {Status}.", (int)response.StatusCode);
                return Unavailable("Google refused the connection.");
            }

            return Read(body, connection);
        }
    }

    /// <summary>
    /// Renews the access token with the stored refresh token. Google answering invalid_grant means the owner took the access away (or
    /// the token was revoked), which only the owner can fix by connecting again, so it is reauthorization_required (409). Everything
    /// else is Google unavailable (503). Made once and never retried.
    /// </summary>
    public async Task<Result<GoogleCalendarAccess>> RefreshAsync(string refreshToken, McpProviderOAuth connection, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, connection.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
                ["grant_type"] = "refresh_token",
            }),
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Google's token endpoint is unreachable for a refresh.");
            return RefreshUnavailable("Google could not be reached.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogError("Google's token endpoint timed out on a refresh.");
            return RefreshUnavailable("Google did not answer in time.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.BadRequest && ErrorOf(body) == "invalid_grant")
            {
                logger.LogWarning("Google refused a calendar refresh token: invalid_grant. The owner must connect again.");
                return Result<GoogleCalendarAccess>.Failure("Google no longer accepts this calendar connection. Connect the calendar again.", McpFailureCodes.ReauthorizationRequired);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Google's token endpoint answered {Status} to a refresh.", (int)response.StatusCode);
                return RefreshUnavailable("Google refused the renewal.");
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var access = Text(document.RootElement, "access_token");
                if (string.IsNullOrWhiteSpace(access)
                    || !document.RootElement.TryGetProperty("expires_in", out var lifetime) || !lifetime.TryGetInt32(out var seconds) || seconds <= 0)
                {
                    logger.LogError("Google's refresh answer is missing a field it documents.");
                    return RefreshUnavailable("Google answered something unexpected.");
                }

                // A vendor that rotates its refresh tokens invalidates nothing on our side, but the next renewal needs the new one.
                // Missing it would work until the stored token aged out and then strand the tenant, so it is a failure and not a shrug.
                var rotated = Text(document.RootElement, "refresh_token");
                if (connection.RotatesRefreshToken && string.IsNullOrWhiteSpace(rotated))
                {
                    logger.LogError("{Provider} renews by rotation and returned no refresh token.", connection.DisplayName);
                    return RefreshUnavailable($"{connection.DisplayName} did not return a refresh token to keep.");
                }

                return Result<GoogleCalendarAccess>.Success(
                    new GoogleCalendarAccess(access, clock.GetUtcNow().AddSeconds(seconds), string.IsNullOrWhiteSpace(rotated) ? null : rotated));
            }
            catch (JsonException)
            {
                logger.LogError("Google's refresh answer is not JSON.");
                return RefreshUnavailable("Google answered something unexpected.");
            }
        }
    }

    private static Result<GoogleCalendarAccess> RefreshUnavailable(string message) =>
        Result<GoogleCalendarAccess>.Failure(message, McpFailureCodes.OAuthUnavailable);

    private Result<GoogleCalendarTokens> Read(string body, McpProviderOAuth connection)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            logger.LogError("Google's token endpoint answered something that is not JSON.");
            return Unavailable("Google answered something unexpected.");
        }

        using (document)
        {
            var root = document.RootElement;
            var access = Text(root, "access_token");
            var scope = Text(root, "scope");
            if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(scope)
                || !root.TryGetProperty("expires_in", out var lifetime) || !lifetime.TryGetInt32(out var seconds) || seconds <= 0)
            {
                logger.LogError("Google's token answer is missing a field it documents.");
                return Unavailable("Google answered something unexpected.");
            }

            // A person can untick a scope on the consent screen. Every scope this connection asks for is one something needs, so a
            // partial grant is refused here rather than surfacing later as a tool error or a record that could not be written.
            var granted = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (connection.Scopes.Any(required => !granted.Contains(required, StringComparer.Ordinal)))
                return Result<GoogleCalendarTokens>.Failure(connection.PartialGrantMessage, McpFailureCodes.ScopeNotGranted);

            var refresh = Text(root, "refresh_token");
            if (string.IsNullOrWhiteSpace(refresh))
            {
                return Result<GoogleCalendarTokens>.Failure(
                    "Google did not return a refresh token, so the connection could not be kept alive.",
                    McpFailureCodes.RefreshTokenMissing);
            }

            return Result<GoogleCalendarTokens>.Success(new GoogleCalendarTokens(access, refresh, clock.GetUtcNow().AddSeconds(seconds)));
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private string? ErrorOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return Text(document.RootElement, "error");
        }
        catch (JsonException)
        {
            logger.LogWarning("Google's error answer is not JSON, so its error code could not be read.");
            return null;
        }
    }

    private static Result<GoogleCalendarTokens> Unavailable(string message) =>
        Result<GoogleCalendarTokens>.Failure(message, McpFailureCodes.OAuthUnavailable);
}
