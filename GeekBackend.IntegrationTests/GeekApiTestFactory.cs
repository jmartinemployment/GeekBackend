extern alias GeekApi;

using System.Security.Claims;
using System.Text.Encodings.Web;
using GeekApi::GeekAPI.Services.GeekCrawler;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeekBackend.IntegrationTests;

public sealed class GeekApiTestFactory : WebApplicationFactory<GeekApi::Program>
{
    public static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public InMemoryGeekRepositoryHandler Repository { get; } = new();
    public RagProtocolStubHandler Rag { get; } = new();

    public GeekApiTestFactory()
    {
        Environment.SetEnvironmentVariable("REPO_API_KEY", "integration-test-key");
        Environment.SetEnvironmentVariable("GEEK_BACKEND_API_KEY", "integration-test-backend-key");
        Environment.SetEnvironmentVariable("GEEK_CRAWLER_RAG_URL", "http://rag.test");
        Environment.SetEnvironmentVariable("GEEK_CRAWLER_RAG_API_KEY", "integration-test-rag-key");
        Environment.SetEnvironmentVariable("GEEK_OAUTH_AUTHORITY", "http://oauth.test");
        Environment.SetEnvironmentVariable("CORS_ORIGINS", "https://www.geekatyourspot.com");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("REPO_URL", Environment.GetEnvironmentVariable("TEST_REPO_URL") ?? "http://127.0.0.1:5050");
        builder.UseSetting("REPO_API_KEY", "integration-test-key");
        builder.UseSetting("GEEK_BACKEND_API_KEY", "integration-test-backend-key");
        builder.UseSetting("GEEK_CRAWLER_WORKER_COUNT", "0");
        builder.UseSetting("CORS_ORIGINS", "https://www.geekatyourspot.com");
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REPO_URL"] = Environment.GetEnvironmentVariable("TEST_REPO_URL") ?? "http://127.0.0.1:5050",
                ["REPO_API_KEY"] = "integration-test-key",
                ["GEEK_BACKEND_API_KEY"] = "integration-test-backend-key",
                ["GEEK_CRAWLER_WORKER_COUNT"] = "0",
                ["CORS_ORIGINS"] = "https://www.geekatyourspot.com"
            });
        });
        builder.ConfigureServices(services =>
        {
            Environment.SetEnvironmentVariable("GEEK_BACKEND_API_KEY", "integration-test-backend-key");
            services.AddHttpClient("GeekRepository")
                .ConfigurePrimaryHttpMessageHandler(() => Repository);
            services.AddHttpClient<IGeekCrawlerRagClient, HttpGeekCrawlerRagClient>(client =>
                {
                    client.BaseAddress = new Uri("http://rag.test/");
                    client.DefaultRequestHeaders.Remove("X-Api-Key");
                    client.DefaultRequestHeaders.TryAddWithoutValidation(
                        "X-Api-Key",
                        "integration-test-rag-key");
                })
                .ConfigurePrimaryHttpMessageHandler(() => Rag);

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultScheme = TestAuthenticationHandler.SchemeName;
            });
        });
    }

    public HttpClient CreateAuthenticatedClient(Guid? userId = null)
    {
        var client = CreateClient();
        Environment.SetEnvironmentVariable("GEEK_BACKEND_API_KEY", "integration-test-backend-key");
        client.DefaultRequestHeaders.Add("X-API-Key", "integration-test-backend-key");
        client.DefaultRequestHeaders.Add("X-Geek-User-Id", (userId ?? OwnerUserId).ToString("D"));
        return client;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "IntegrationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = Request.Query["access_token"].ToString();
            if (string.IsNullOrWhiteSpace(token)
                && Request.Headers.Authorization.ToString().StartsWith(
                    "Bearer ",
                    StringComparison.OrdinalIgnoreCase))
            {
                token = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();
            }
            if (!Guid.TryParse(token, out var userId))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                [
                    new Claim("sub", userId.ToString("D")),
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                ],
                SchemeName);
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
