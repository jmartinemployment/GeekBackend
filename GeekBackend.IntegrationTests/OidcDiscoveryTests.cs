using System.Net;

namespace GeekBackend.IntegrationTests;

public sealed class OidcDiscoveryTests : IClassFixture<GeekApiTestFactory>
{
    private readonly HttpClient _client;

    public OidcDiscoveryTests(GeekApiTestFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task OpenIdConfiguration_ReturnsIssuerAndHttpsEndpoints()
    {
        var response = await _client.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("https://test.geekatyourspot.com", json, StringComparison.Ordinal);
        Assert.Contains("/connect/token", json, StringComparison.Ordinal);
        Assert.Contains("/connect/authorize", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
