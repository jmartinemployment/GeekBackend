using System.Net;
using System.Text.Json;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Mcp;

/// <summary>What Google handed back for an approved connection. The refresh token is what keeps the connection alive after the access token lapses.</summary>
public sealed record GoogleCalendarTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

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
    public string AuthorizationUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId!,
            ["redirect_uri"] = options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', GoogleCalendarOptions.Scopes),
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "false",
            ["state"] = state,
        };
        var encoded = string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return $"{GoogleCalendarOptions.AuthorizeEndpoint}?{encoded}";
    }

    public async Task<Result<GoogleCalendarTokens>> ExchangeCodeAsync(string code, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GoogleCalendarOptions.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = options.ClientId!,
                ["client_secret"] = options.ClientSecret!,
                ["redirect_uri"] = options.RedirectUri,
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

            return Read(body);
        }
    }

    private Result<GoogleCalendarTokens> Read(string body)
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

            // A person can untick a scope on the consent screen. Booking cannot work without the one that writes events.
            if (!scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(GoogleCalendarOptions.EventsScope, StringComparer.Ordinal))
            {
                return Result<GoogleCalendarTokens>.Failure(
                    "Calendar access was not granted. The receptionist cannot book without permission to create events.",
                    McpFailureCodes.ScopeNotGranted);
            }

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

    private static string? ErrorOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return Text(document.RootElement, "error");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Result<GoogleCalendarTokens> Unavailable(string message) =>
        Result<GoogleCalendarTokens>.Failure(message, McpFailureCodes.OAuthUnavailable);
}
