extern alias GeekApi;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using GeekApi::GeekAPI.Services.GeekCrawler;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace GeekBackend.IntegrationTests;

public sealed class GeekApiTestFactory : WebApplicationFactory<GeekApi::Program>
{
    public static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // A test-only symmetric key. Program.cs's real AddJwtBearer() validates against
    // GEEK_OAUTH_AUTHORITY's discovery metadata, which "http://oauth.test" cannot serve — there is
    // no real issuer in this test process. ConfigureWebHost below replaces that validation with
    // this key instead of trying to stand up a fake OIDC server, so a policy pinned to the real
    // "Bearer" scheme (ContentCreatorAuthConstants.ManagePolicy explicitly is, and should stay
    // that way in production) can still be exercised end to end.
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("integration-test-signing-key-at-least-32-bytes-long"));
    private const string Issuer = "http://oauth.test";

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
                ["CORS_ORIGINS"] = "https://www.geekatyourspot.com",
                ["GccV2Skills:AdminUserIds"] = OwnerUserId.ToString("D"),
                ["GccV2Agents:SnapshotSigningKey"] = "integration-agent-signing-key-000001",
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

            // Program.cs's own AddJwtBearer() registered the real "Bearer" scheme against
            // GEEK_OAUTH_AUTHORITY — required now (GeekAPI refuses to start without it), but this
            // process is not a real GeekOAuth, so nothing can serve that authority's discovery
            // metadata. PostConfigure here — running after Program.cs's own configuration — swaps
            // the validation to the symmetric key above, so a token minted by IssueTestToken below
            // validates the same way ContentCreatorAuthConstants.ManagePolicy expects a real one to:
            // through the actual "Bearer" scheme the policy is pinned to, not a same-effect stand-in
            // registered under a different name.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = SigningKey,
                    NameClaimType = "sub",
                    ClockSkew = TimeSpan.FromMinutes(1),
                };

                // Program.cs's own AddJwtBearer() already ran, and its internal
                // JwtBearerPostConfigureOptions built a ConfigurationManager that fetches discovery
                // metadata from GEEK_OAUTH_AUTHORITY over HTTP — nothing in this test process can
                // serve that. Setting Authority/MetadataAddress to null here does not undo an
                // already-built ConfigurationManager; it is a separate object, constructed once,
                // that JwtBearerHandler reads directly at request time regardless of those two
                // properties. Replacing the ConfigurationManager reference itself is what actually
                // takes effect, with a static config carrying only the signing key this process
                // controls — no HTTP fetch, so ordering relative to the framework's own
                // PostConfigure stops mattering.
                var staticConfiguration =
                    new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
                    {
                        Issuer = Issuer,
                    };
                staticConfiguration.SigningKeys.Add(SigningKey);
                options.ConfigurationManager =
                    new StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                        staticConfiguration);
            });
        });
    }

    /// <summary>
    /// A signed token content-creator.manage's [Authorize] policy — pinned to the real "Bearer"
    /// scheme — will actually authenticate, carrying the given scopes as one space-delimited claim
    /// the same way a real GeekOAuth token does.
    /// </summary>
    public static string IssueTestToken(Guid userId, params string[] scopes)
    {
        var claims = new List<Claim> { new("sub", userId.ToString("D")) };
        if (scopes.Length > 0)
            claims.Add(new Claim("scope", string.Join(' ', scopes)));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public HttpClient CreateAuthenticatedClient(Guid? userId = null)
    {
        var client = CreateClient();
        Environment.SetEnvironmentVariable("GEEK_BACKEND_API_KEY", "integration-test-backend-key");
        client.DefaultRequestHeaders.Add("X-API-Key", "integration-test-backend-key");
        client.DefaultRequestHeaders.Add("X-Geek-User-Id", (userId ?? OwnerUserId).ToString("D"));
        return client;
    }

    /// <summary>
    /// A client whose token carries the given scopes, for testing GeekOAuth-scope-gated policies
    /// such as <c>ContentCreatorAuthConstants.ManagePolicy</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="TestAuthenticationHandler"/> stands in for real JWT validation, and its token is
    /// just a GUID — there is no JWT to carry a scope claim inside. <c>X-Test-Scopes</c> is a
    /// test-only side channel the handler reads to add one, so a policy that inspects the "scope"
    /// claim can be exercised without standing up a real token issuer. It does nothing outside the
    /// Testing environment this factory configures.
    /// </remarks>
    /// <summary>
    /// Distinct from <see cref="CreateAuthenticatedClient(Guid?)"/> by name, not just by an extra
    /// parameter: <c>CreateAuthenticatedClient(userId)</c> silently resolves to the non-params
    /// overload even when a caller means "and no scopes" — params overload resolution prefers the
    /// exact-arity match. That ambiguity cost real time here: a test that meant to send zero
    /// scopes was actually calling the X-API-Key/X-Geek-User-Id overload, sending no JWT at all,
    /// and its 401 looked exactly like a correctly-enforced "no scope" case until compared against
    /// one that could not make the same mistake.
    /// </summary>
    public HttpClient CreateScopedClient(Guid userId, params string[] scopes)
    {
        var client = CreateAuthenticatedClient(userId);

        // ContentCreatorAuthConstants.ManagePolicy is pinned to the real "Bearer" scheme
        // (AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)) — deliberately, so
        // production is never at the mercy of whatever DefaultAuthenticateScheme happens to be.
        // Satisfying it needs an actual signed token that scheme's validator accepts, not the bare
        // GUID the base overload's X-API-Key/X-Geek-User-Id path uses for ApiKeyMiddleware.
        var token = IssueTestToken(userId, scopes);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

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
