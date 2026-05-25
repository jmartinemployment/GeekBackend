extern alias GeekApi;
extern alias GeekRepo;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// Starts in-process GeekRepository + GeekAPI wired together for OAuth E2E tests.
/// Requires TEST_DATABASE_URL (PostgreSQL with users table from EF migrations).
/// </summary>
public sealed class GeekCombinedFixture : IAsyncLifetime, IDisposable
{
    private const string RepoApiKey = "integration-test-key";
    private WebApplicationFactory<GeekRepo::Program>? _repoFactory;
    private WebApplicationFactory<GeekApi::Program>? _apiFactory;

    public HttpClient ApiClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        Environment.SetEnvironmentVariable("DATABASE_URL", IntegrationTestConfig.DatabaseUrl);
        Environment.SetEnvironmentVariable("TEST_DATABASE_URL", IntegrationTestConfig.DatabaseUrl);
        Environment.SetEnvironmentVariable("REPO_API_KEY", RepoApiKey);
        Environment.SetEnvironmentVariable("GEEK_API_CLIENT_SECRET", "integration-geekapi-secret");
        Environment.SetEnvironmentVariable("DISABLE_PLAYWRIGHT", "true");
        Environment.SetEnvironmentVariable("AUTH_SERVER_URL", "https://test.geekatyourspot.com");
        Environment.SetEnvironmentVariable("GEEK_WEBSITE_CLIENT_SECRET", "integration-website-secret");
        Environment.SetEnvironmentVariable("GEEK_RESOURCE_SERVER_SECRET", "integration-resource-secret");
        Environment.SetEnvironmentVariable("CORS_ORIGINS", "https://seo.geekatyourspot.com,http://127.0.0.1");

        await SqlMigrationTestHelper.ApplyAsync();

        _repoFactory = new WebApplicationFactory<GeekRepo::Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("DATABASE_URL", IntegrationTestConfig.DatabaseUrl);
            });

        var repoBase = _repoFactory.Server.BaseAddress!.ToString().TrimEnd('/');

        _apiFactory = new WebApplicationFactory<GeekApi::Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("AUTH_SERVER_URL", "https://test.geekatyourspot.com");
                builder.UseSetting("REPO_URL", repoBase);
                builder.UseSetting("REPO_API_KEY", RepoApiKey);
                builder.UseSetting("GEEK_API_CLIENT_SECRET", "integration-geekapi-secret");
                builder.UseSetting("GEEK_WEBSITE_CLIENT_SECRET", "integration-website-secret");
                builder.UseSetting("GEEK_RESOURCE_SERVER_SECRET", "integration-resource-secret");
                builder.UseSetting("CORS_ORIGINS", "https://seo.geekatyourspot.com,http://127.0.0.1");

                builder.ConfigureServices(services =>
                {
                    var toRemove = services
                        .Where(d => d.ServiceType == typeof(IHostedService)
                            && d.ImplementationType?.Name == "JtiCleanupWorker")
                        .ToList();
                    foreach (var descriptor in toRemove)
                        services.Remove(descriptor);
                });
            });

        ApiClient = _apiFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        // Warm up hosted services (OpenIddict client seeder).
        _ = await ApiClient.GetAsync("/.well-known/openid-configuration");
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        ApiClient?.Dispose();
        _apiFactory?.Dispose();
        _repoFactory?.Dispose();
    }
}
