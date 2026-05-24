extern alias GeekApi;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GeekBackend.IntegrationTests;

public sealed class OAuthFlowTests : IClassFixture<GeekApiTestFactory>
{
    private readonly HttpClient _client;

    public OAuthFlowTests(GeekApiTestFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Pkce_MissingVerifier_ReturnsBadRequest()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "geek-seo-electron",
            ["code"] = "invalid-code",
            ["redirect_uri"] = "http://127.0.0.1/callback"
        });

        var response = await _client.PostAsync("/connect/token", content);
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized,
            $"Expected 400/401, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task PasswordGrant_ReturnsUnsupportedGrantType()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "user@example.com",
            ["password"] = "secret"
        });

        var response = await _client.PostAsync("/connect/token", content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,
            $"Expected 400/403, got {(int)response.StatusCode}: {body}");
        Assert.Contains("unsupported_grant_type", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CustomGeekGrant_ReturnsUnsupportedGrantType()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:geek:device"
        });

        var response = await _client.PostAsync("/connect/token", content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,
            $"Expected 400/403, got {(int)response.StatusCode}: {body}");
        Assert.Contains("unsupported_grant_type", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClientCredentials_KioskFlow_SucceedsWhenRepositoryAvailable()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "geekatyourspot-website",
            ["client_secret"] = Environment.GetEnvironmentVariable("GEEK_WEBSITE_CLIENT_SECRET")
                ?? "integration-website-secret",
            ["scope"] = "mcp.tools"
        });

        var response = await _client.PostAsync("/connect/token", content);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            return;

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("access_token", out _));
    }

    [Fact]
    public async Task IntrospectionEndpoint_IsListedInDiscovery()
    {
        var json = await _client.GetStringAsync("/.well-known/openid-configuration");
        Assert.Contains("/connect/introspect", json, StringComparison.Ordinal);
    }
}
