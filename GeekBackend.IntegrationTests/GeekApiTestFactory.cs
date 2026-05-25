extern alias GeekApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GeekBackend.IntegrationTests;

public sealed class GeekApiTestFactory : WebApplicationFactory<GeekApi::Program>
{
    public GeekApiTestFactory()
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
            var cleanupWorkers = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType?.Name == "JtiCleanupWorker")
                .ToList();
            foreach (var descriptor in cleanupWorkers)
                services.Remove(descriptor);
        });
    }
}
