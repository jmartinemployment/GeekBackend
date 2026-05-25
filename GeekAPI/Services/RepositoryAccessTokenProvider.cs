using System.Net.Http.Headers;
using System.Text.Json;
using GeekApplication.Auth;

namespace GeekAPI.Services;

public sealed class RepositoryAccessTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tokenEndpoint;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public RepositoryAccessTokenProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _clientId = GeekOAuthConstants.GeekApiClientId;
        _clientSecret = Environment.GetEnvironmentVariable("GEEK_API_CLIENT_SECRET") ?? string.Empty;
        // GeekAPI is the issuer — use loopback so startup seeders and deploy healthchecks do not
        // call the public URL before this instance is accepting traffic (502 during rollouts).
        var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
        _tokenEndpoint = string.IsNullOrWhiteSpace(_clientSecret)
            ? string.Empty
            : $"http://127.0.0.1:{port}/connect/token";
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_clientSecret)
            || string.IsNullOrWhiteSpace(_tokenEndpoint))
        {
            return null;
        }

        if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-30))
            return _accessToken;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-30))
                return _accessToken;

            var client = _httpClientFactory.CreateClient("GeekRepositoryToken");
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["scope"] = GeekOAuthConstants.InternalApiScope
            });

            using var response = await client.PostAsync(_tokenEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("access_token", out var tokenElement))
                return null;

            _accessToken = tokenElement.GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
                ? expiresElement.GetInt32()
                : 300;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}

public sealed class RepositoryBearerTokenHandler : DelegatingHandler
{
    private readonly RepositoryAccessTokenProvider _tokenProvider;

    public RepositoryBearerTokenHandler(RepositoryAccessTokenProvider tokenProvider) =>
        _tokenProvider = tokenProvider;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
