extern alias GeekApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GeekBackend.IntegrationTests;

public sealed class GeekApiTestFactory : WebApplicationFactory<GeekApi::Program>
{
    public GeekApiTestFactory()
    {
        Environment.SetEnvironmentVariable("REPO_API_KEY", "integration-test-key");
        Environment.SetEnvironmentVariable("GEEK_BACKEND_API_KEY", "integration-test-backend-key");
        Environment.SetEnvironmentVariable("CORS_ORIGINS", "https://www.geekatyourspot.com");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("REPO_URL", Environment.GetEnvironmentVariable("TEST_REPO_URL") ?? "http://127.0.0.1:5050");
        builder.UseSetting("REPO_API_KEY", "integration-test-key");
        builder.UseSetting("GEEK_BACKEND_API_KEY", "integration-test-backend-key");
        builder.UseSetting("CORS_ORIGINS", "https://www.geekatyourspot.com");
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REPO_URL"] = Environment.GetEnvironmentVariable("TEST_REPO_URL") ?? "http://127.0.0.1:5050",
                ["REPO_API_KEY"] = "integration-test-key",
                ["GEEK_BACKEND_API_KEY"] = "integration-test-backend-key",
                ["CORS_ORIGINS"] = "https://www.geekatyourspot.com"
            });
        });
    }
}
