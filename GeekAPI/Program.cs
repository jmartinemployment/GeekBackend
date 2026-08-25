using System.Text.Json.Serialization;
using DotNetEnv;
using GeekAPI.Auth;
using GeekAPI.Controllers;
using GeekAPI.Controllers.ContentCreatorV2.Auth;
using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using GeekAPI.Extensions;
using GeekAPI.HttpClients;
using GeekAPI.Middleware;
using GeekAPI.Services;
using GeekAPI.Services.ContentCreatorV2;
using GeekAPI.Services.ContentWriterV3;
using GeekAPI.Services.SiteAnalyzer2;
using GeekAPI.Services.Workflow.Hosting;
using GeekAPI.Services.Workflow.Infrastructure;
using GeekApplication.Interfaces;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekSa2Read.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddOpenApi();
builder.Services.AddControllers()
    // Workflow controllers live in this assembly (GeekAPI.Controllers.Workflow). JSON options
    // keep string enums + camelCase for parity with the prior Workflow/content-writer contract
    // GeekContentCreator already consumes.
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new GeekAPI.Services.Workflow.Domain.Entities.TolerantNullableLedeTypeConverter());
        options.JsonSerializerOptions.Converters.Add(new GeekAPI.Services.Workflow.Domain.Entities.StrictLedeTypeConverter());
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();

var corsOrigins = CorsOriginParser.GetAllowedOrigins();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .SetIsOriginAllowed(CorsOriginParser.IsOriginAllowed)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()));

var repoUrl = Environment.GetEnvironmentVariable("REPO_URL") ?? "http://localhost:5050";
var repoApiKey = Environment.GetEnvironmentVariable("REPO_API_KEY") ?? string.Empty;
var repositoryClientBuilder = builder.Services.AddHttpClient("GeekRepository", client =>
    client.BaseAddress = new Uri(repoUrl));
if (!string.IsNullOrWhiteSpace(repoApiKey))
{
    repositoryClientBuilder.ConfigureHttpClient(client =>
        client.DefaultRequestHeaders.Add("X-Repo-Key", repoApiKey));
}

builder.Services.AddScoped<ICaseStudyRepository, HttpCaseStudyRepository>();
builder.Services.AddScoped<IDepartmentRepository, HttpDepartmentRepository>();
builder.Services.AddScoped<IUseCaseRepository, HttpUseCaseRepository>();
builder.Services.AddScoped<IBlogRepository, HttpBlogRepository>();
builder.Services.AddScoped<IWebPostRepository, HttpWebPostRepository>();
builder.Services.AddScoped<IAssetUploadService, NoOpAssetUploadService>();

// Content Writer V3: HTTP client proxy to GeekRepository
builder.Services.AddScoped(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("GeekRepository");
    var logger = sp.GetRequiredService<ILogger<HttpContentWriterV3Repository>>();
    return new HttpContentWriterV3Repository(httpClient, logger);
});

builder.Services.AddScoped(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("GeekRepository");
    var logger = sp.GetRequiredService<ILogger<HttpGccRepository>>();
    return new HttpGccRepository(httpClient, logger);
});
builder.Services.AddScoped<GeekAPI.Services.ContentCreator.GccGenerateService>();
builder.Services.AddSingleton<GeekAPI.Services.ContentCreator.GccJobStore>();
builder.Services.AddContentCreatorV2();

// GeekOAuth-issued JWT bearer, needed only so the v2 realtime hub can require [Authorize]
// (ApiKeyMiddleware's header-based auth can't run over a WebSocket upgrade). Additive: existing
// routes keep authenticating exactly as before via ApiKeyMiddleware.
var gccV2HubAuthority = (Environment.GetEnvironmentVariable("GEEK_OAUTH_AUTHORITY")
    ?? Environment.GetEnvironmentVariable("AUTH_SERVER_URL")
    ?? string.Empty).Trim().TrimEnd('/');
if (!string.IsNullOrWhiteSpace(gccV2HubAuthority))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = gccV2HubAuthority;
            options.RequireHttpsMetadata = !gccV2HubAuthority.Contains("localhost", StringComparison.OrdinalIgnoreCase);
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = false,
                ValidateLifetime = true,
                NameClaimType = "sub",
                ClockSkew = TimeSpan.FromMinutes(1),
            };
            GccV2JwtHubQueryToken.AcceptAccessTokenFromQuery(options);
        });
    builder.Services.AddAuthorization();
}
else
{
    Console.WriteLine("GEEK_OAUTH_AUTHORITY/AUTH_SERVER_URL not set — /hubs/gcc-v2-realtime will reject all connections.");
}

var geekSeoUrl = (Environment.GetEnvironmentVariable("GEEK_SEO_API_URL") ?? "").Trim().TrimEnd('/');
builder.Services.AddHttpClient<GeekAPI.Services.ContentCreator.HttpGeekSeoSiteAnalyzerClient>(client =>
{
    if (!string.IsNullOrWhiteSpace(geekSeoUrl))
    {
        client.BaseAddress = new Uri(geekSeoUrl + "/");
        client.Timeout = TimeSpan.FromMinutes(2);
    }
});

var imageGeneratorBaseUrl =
    Environment.GetEnvironmentVariable("IMAGE_GENERATOR_BASE_URL")
    ?? "https://geek-image-generator.geekatyourspot.com";
builder.Services.AddTransient<GeekAPI.Services.Auth.GeekOAuthTokenHandler>();
builder.Services.AddHttpClient<GeekAPI.Services.Gcw.HttpImageGeneratorClient>(client =>
{
    client.BaseAddress = new Uri(imageGeneratorBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(3);
})
    // /api/generate spends money with paid providers, so it authenticates every caller. The
    // handler attaches a client-credentials token, keeping HttpImageGeneratorClient unaware of it.
    .AddHttpMessageHandler<GeekAPI.Services.Auth.GeekOAuthTokenHandler>();

// Content Writer V3: Services
// Provider-selectable generation: keyed registrations resolved via IContentGeneratorFactory.
// OpenAi reuses Workflow's already-bound LlmProvidersOptions (registered by AddWorkflow below).
builder.Services.AddKeyedScoped<IContentGenerator, ClaudeContentGenerator>(ContentGeneratorProvider.Anthropic);
builder.Services.AddKeyedScoped<IContentGenerator, OpenAiContentGenerator>(ContentGeneratorProvider.OpenAi);
builder.Services.AddScoped<IContentGeneratorFactory, ContentGeneratorFactory>();
builder.Services.AddScoped<IAnalyticsAdapter, GoogleAnalyticsAdapter>();
builder.Services.AddScoped<IPublishAdapter, WordPressPublishAdapter>();
builder.Services.AddScoped<INotificationService, NotificationService>();

builder.Services.AddScoped<DepartmentContentService>();
builder.Services.AddGeekSa2Read();
builder.Services.AddScoped<SiteAnalyzer2SiteProfileReader>();

// Workflow (GeekAPI-owned): persistence reuses the "GeekRepository" named HttpClient already
// configured above (X-Repo-Key already attached). See GeekBackend/AGENTS.md § Service topology
// and the content-writer "copy, never reuse" rule.
builder.Services.AddWorkflow(builder.Configuration,
    sp => new GeekRepositoryPersistenceStore(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<GeekRepositoryPersistenceStore>>()));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        throw;
    }
});

app.Logger.LogInformation("CORS origins: {Origins}", string.Join(", ", corsOrigins));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Environment.IsProduction())
    app.UseHttpsRedirection();

app.UseCors();

// Only wired when GEEK_OAUTH_AUTHORITY/AUTH_SERVER_URL is configured — see registration above.
// Scoped to the v2 hub: ApiKeyMiddleware below still authenticates every other route exactly as
// it did before this file was touched.
if (!string.IsNullOrWhiteSpace(gccV2HubAuthority))
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseMiddleware<LegacyAuthRetiredMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

if (!string.IsNullOrWhiteSpace(gccV2HubAuthority))
{
    app.MapHub<GccV2RealtimeHub>("/hubs/gcc-v2-realtime");
}

// Workflow: loads persisted projects/clients from GeekRepository at startup.
await app.HydrateWorkflowAsync();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

public partial class Program;
