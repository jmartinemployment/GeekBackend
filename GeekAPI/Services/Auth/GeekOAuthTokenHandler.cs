using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GeekAPI.Services.Auth;

/// <summary>
/// Attaches a GeekOAuth client-credentials token to every outbound request on the handler's
/// client, so callers do not each have to remember to authenticate.
/// <para>
/// Uses the existing <c>geekapi</c> M2M client (seeded in GeekOAuth's ClientSeeder with scope
/// <c>internal.api</c>). The token is cached until shortly before expiry — GeekOAuth issues a
/// 60-minute lifetime for M2M clients, so this is one token request per hour, not per call.
/// </para>
/// </summary>
public sealed class GeekOAuthTokenHandler : DelegatingHandler
{
    private const string Scope = "internal.api";
    private static readonly TimeSpan ExpiryGuard = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GeekOAuthTokenHandler> _logger;
    private readonly string _tokenUrl;
    private readonly string _clientId;
    private readonly string _clientSecret;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public GeekOAuthTokenHandler(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<GeekOAuthTokenHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var authUrl = (config["AUTH_URL"] ?? "https://auth.geekatyourspot.com").TrimEnd('/');
        _tokenUrl = $"{authUrl}/connect/token";
        _clientId = config["OAUTH_CLIENT_ID"] ?? "geekapi";
        _clientSecret = config["CLIENT_SECRET_GEEKAPI"]
            ?? throw new InvalidOperationException(
                "CLIENT_SECRET_GEEKAPI is required to call internal APIs.");
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
        return await base.SendAsync(request, ct);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt) return _token;

        await _lock.WaitAsync(ct);
        try
        {
            // Re-check inside the lock: concurrent callers must not each fetch a token.
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt) return _token;

            using var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsync(
                _tokenUrl,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["scope"] = Scope,
                }),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                // Never log the body: it can echo client_secret back on some errors.
                _logger.LogError(
                    "GeekOAuth token request failed with {Status} for client {ClientId}",
                    (int)response.StatusCode, _clientId);
                throw new HttpRequestException(
                    $"GeekOAuth token request failed ({(int)response.StatusCode}).");
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new HttpRequestException("GeekOAuth returned an empty token response.");

            _token = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn) - ExpiryGuard;
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
