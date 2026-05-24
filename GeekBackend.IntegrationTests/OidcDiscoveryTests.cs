extern alias GeekApi;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GeekBackend.IntegrationTests;

public sealed class OidcDiscoveryTests : IClassFixture<OidcDiscoveryTests.GeekApiFactory>
{
    private readonly HttpClient _client;

    public OidcDiscoveryTests(GeekApiFactory factory) => _client = factory.CreateClient();

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

    public sealed class GeekApiFactory : WebApplicationFactory<GeekApi::Program>
    {
        public GeekApiFactory()
        {
            Environment.SetEnvironmentVariable("AUTH_SERVER_URL", "https://test.geekatyourspot.com");
            Environment.SetEnvironmentVariable("REPO_API_KEY", "integration-test-key");
            Environment.SetEnvironmentVariable("GEEK_WEBSITE_CLIENT_SECRET", "integration-website-secret");
            Environment.SetEnvironmentVariable("GEEK_RESOURCE_SERVER_SECRET", "integration-resource-secret");
            Environment.SetEnvironmentVariable("CORS_ORIGINS", "https://seo.geekatyourspot.com");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("AUTH_SERVER_URL", "https://test.geekatyourspot.com");
            builder.UseSetting("REPO_URL", Environment.GetEnvironmentVariable("TEST_REPO_URL") ?? "http://127.0.0.1:5050");
            builder.UseSetting("REPO_API_KEY", "integration-test-key");
            builder.UseSetting("GEEK_WEBSITE_CLIENT_SECRET", "integration-website-secret");
            builder.UseSetting("GEEK_RESOURCE_SERVER_SECRET", "integration-resource-secret");
            builder.UseSetting("CORS_ORIGINS", "https://seo.geekatyourspot.com");
            builder.ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AUTH_SERVER_URL"] = "https://test.geekatyourspot.com",
                    ["REPO_URL"] = Environment.GetEnvironmentVariable("TEST_REPO_URL") ?? "http://127.0.0.1:5050",
                    ["REPO_API_KEY"] = "integration-test-key",
                    ["GEEK_WEBSITE_CLIENT_SECRET"] = "integration-website-secret",
                    ["GEEK_RESOURCE_SERVER_SECRET"] = "integration-resource-secret",
                    ["CORS_ORIGINS"] = "https://seo.geekatyourspot.com"
                });
            });

            builder.ConfigureServices(services =>
            {
                var hosted = services
                    .Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
                    .ToList();
                foreach (var descriptor in hosted)
                    services.Remove(descriptor);
            });
        }
    }
}
