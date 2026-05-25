extern alias GeekRepo;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories.JtiBlacklist;
using Microsoft.AspNetCore.WebUtilities;

namespace GeekBackend.IntegrationTests;

[Collection(nameof(GeekCombinedFixture))]
public sealed class OAuthEndToEndTests : IClassFixture<GeekCombinedFixture>
{
    private const string ClientId = "geek-seo-electron";
    private const string RedirectUri = "http://127.0.0.1/callback";
    private readonly GeekCombinedFixture _fixture;

    public OAuthEndToEndTests(GeekCombinedFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PkceFlow_IssuesTokens()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        var email = $"pkce-{Guid.NewGuid():N}@integration.test";
        var password = "IntegrationTest!1Aa";
        var user = await IntegrationTestDataHelper.CreateUserAsync(email, password);

        try
        {
            var (verifier, challenge) = OAuthPkceHelper.CreatePkcePair();
            var authorizeUrl = OAuthPkceHelper.BuildAuthorizeUrl(
                _fixture.ApiClient.BaseAddress!.ToString(),
                ClientId,
                RedirectUri,
                challenge);

            var tokens = await OAuthPkceHelper.LoginAndExchangeCodeAsync(
                _fixture.ApiClient,
                authorizeUrl,
                email,
                password,
                RedirectUri,
                verifier,
                ClientId);

            Assert.False(string.IsNullOrWhiteSpace(tokens.Access_token));
            Assert.False(string.IsNullOrWhiteSpace(tokens.Refresh_token));
        }
        finally
        {
            await IntegrationTestDataHelper.DeleteUserAsync(user.User.Id);
        }
    }

    [Fact]
    public async Task RefreshToken_Rotates()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        var email = $"refresh-{Guid.NewGuid():N}@integration.test";
        var password = "IntegrationTest!1Aa";
        var user = await IntegrationTestDataHelper.CreateUserAsync(email, password);

        try
        {
            var (verifier, challenge) = OAuthPkceHelper.CreatePkcePair();
            var authorizeUrl = OAuthPkceHelper.BuildAuthorizeUrl(
                _fixture.ApiClient.BaseAddress!.ToString(),
                ClientId,
                RedirectUri,
                challenge);

            var initial = await OAuthPkceHelper.LoginAndExchangeCodeAsync(
                _fixture.ApiClient,
                authorizeUrl,
                email,
                password,
                RedirectUri,
                verifier,
                ClientId);

            var firstRefresh = initial.Refresh_token
                ?? throw new InvalidOperationException("Initial refresh_token missing.");

            using var refreshContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = firstRefresh
            });

            var refreshResponse = await _fixture.ApiClient.PostAsync("/connect/token", refreshContent);
            var refreshBody = await refreshResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

            var rotated = JsonSerializer.Deserialize<OAuthTokenResponse>(refreshBody, JsonSerializerOptions.Web);
            Assert.False(string.IsNullOrWhiteSpace(rotated?.Refresh_token));
            Assert.NotEqual(firstRefresh, rotated!.Refresh_token);
        }
        finally
        {
            await IntegrationTestDataHelper.DeleteUserAsync(user.User.Id);
        }
    }

    [Fact]
    public async Task RefreshToken_Reuse_RevokesAll()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        var email = $"reuse-{Guid.NewGuid():N}@integration.test";
        var password = "IntegrationTest!1Aa";
        var user = await IntegrationTestDataHelper.CreateUserAsync(email, password);

        try
        {
            var (verifier, challenge) = OAuthPkceHelper.CreatePkcePair();
            var authorizeUrl = OAuthPkceHelper.BuildAuthorizeUrl(
                _fixture.ApiClient.BaseAddress!.ToString(),
                ClientId,
                RedirectUri,
                challenge);

            var initial = await OAuthPkceHelper.LoginAndExchangeCodeAsync(
                _fixture.ApiClient,
                authorizeUrl,
                email,
                password,
                RedirectUri,
                verifier,
                ClientId);

            var staleRefresh = initial.Refresh_token
                ?? throw new InvalidOperationException("Initial refresh_token missing.");

            using var rotateContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = staleRefresh
            });
            var rotateResponse = await _fixture.ApiClient.PostAsync("/connect/token", rotateContent);
            Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);

            using var reuseContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = staleRefresh
            });
            var reuseResponse = await _fixture.ApiClient.PostAsync("/connect/token", reuseContent);
            Assert.True(
                reuseResponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized,
                $"Expected refresh reuse to fail, got {(int)reuseResponse.StatusCode}");
        }
        finally
        {
            await IntegrationTestDataHelper.DeleteUserAsync(user.User.Id);
        }
    }

    [Fact]
    public async Task TwoFactor_Required_Redirects()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        var email = $"2fa-{Guid.NewGuid():N}@integration.test";
        var password = "IntegrationTest!1Aa";
        var user = await IntegrationTestDataHelper.CreateUserAsync(email, password, twoFactorEnabled: true);

        try
        {
            var (_, challenge) = OAuthPkceHelper.CreatePkcePair();
            var authorizeUrl = OAuthPkceHelper.BuildAuthorizeUrl(
                _fixture.ApiClient.BaseAddress!.ToString(),
                ClientId,
                RedirectUri,
                challenge);

            var authorizeResponse = await _fixture.ApiClient.GetAsync(authorizeUrl);
            var loginLocation = await ResolveLoginLocationAsync(_fixture.ApiClient, authorizeResponse);
            var returnUrl = ExtractQueryValue(loginLocation, "ReturnUrl") ?? authorizeUrl;

            using var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
                ["Input.ReturnUrl"] = returnUrl,
                ["Input.Button"] = "login"
            });

            var loginPath = loginLocation.IsAbsoluteUri
                ? loginLocation.PathAndQuery
                : loginLocation.ToString();
            var loginResponse = await _fixture.ApiClient.PostAsync(loginPath, loginForm);
            Assert.True(
                OAuthPkceHelper.IsTwoFactorRedirect(loginResponse),
                $"Expected redirect to TwoFactor, got {loginResponse.Headers.Location}");
        }
        finally
        {
            await IntegrationTestDataHelper.DeleteUserAsync(user.User.Id);
        }
    }

    [Fact]
    public async Task JtiBlacklist_RevokedToken_Returns401()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        var email = $"jti-{Guid.NewGuid():N}@integration.test";
        var password = "IntegrationTest!1Aa";
        var user = await IntegrationTestDataHelper.CreateUserAsync(email, password);

        try
        {
            var (verifier, challenge) = OAuthPkceHelper.CreatePkcePair();
            var authorizeUrl = OAuthPkceHelper.BuildAuthorizeUrl(
                _fixture.ApiClient.BaseAddress!.ToString(),
                ClientId,
                RedirectUri,
                challenge,
                scope: "openid offline_access");

            var tokens = await OAuthPkceHelper.LoginAndExchangeCodeAsync(
                _fixture.ApiClient,
                authorizeUrl,
                email,
                password,
                RedirectUri,
                verifier,
                ClientId);

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(tokens.Access_token);
            var jti = jwt.Claims.FirstOrDefault(c => c.Type == "jti")?.Value
                ?? throw new InvalidOperationException("access_token missing jti claim.");

            var factory = new NpgsqlConnectionFactory(IntegrationTestConfig.DatabaseUrl!);
            var blacklist = new PostgresJtiBlacklistRepository(factory);
            var added = await blacklist.AddAsync(jti, DateTime.UtcNow.AddMinutes(10));
            Assert.True(added.IsSuccess, added.Error);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/2fa/setup");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Access_token);
            var apiResponse = await _fixture.ApiClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, apiResponse.StatusCode);
        }
        finally
        {
            await IntegrationTestDataHelper.DeleteUserAsync(user.User.Id);
        }
    }

    private static async Task<Uri> ResolveLoginLocationAsync(HttpClient client, HttpResponseMessage authorizeResponse)
    {
        var current = authorizeResponse;
        for (var i = 0; i < 10; i++)
        {
            if (current.Headers.Location is null)
                break;

            var next = current.Headers.Location.IsAbsoluteUri
                ? current.Headers.Location
                : new Uri(client.BaseAddress!, current.Headers.Location);

            if (next.AbsolutePath.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase))
                return next;

            current = await client.GetAsync(next);
        }

        return new Uri("/Account/Login", UriKind.Relative);
    }

    private static string? ExtractQueryValue(Uri uri, string key)
    {
        var query = QueryHelpers.ParseQuery(uri.Query);
        return query.TryGetValue(key, out var value) ? value.ToString() : null;
    }
}
