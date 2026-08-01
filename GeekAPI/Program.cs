using System.Text.Json.Serialization;
using ContentWriter.Api.Hosting;
using ContentWriter.Infrastructure;
using DotNetEnv;
using GeekAPI.Auth;
using GeekAPI.Controllers;
using GeekAPI.Extensions;
using GeekAPI.HttpClients;
using GeekAPI.Middleware;
using GeekAPI.Services;
using GeekAPI.Services.ContentWriterV3;
using GeekAPI.Services.SiteAnalyzer2;
using GeekApplication.Interfaces;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekSa2Read.DependencyInjection;
using Microsoft.AspNetCore.HttpOverrides;

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
    // content-writer-v2 merge (Phase 1): its controllers live in ContentWriter.Api.dll, a
    // referenced-but-not-entry assembly — ApplicationParts makes MVC discover them. JSON options
    // are additive here: GeekAPI has no enums in any existing response today (confirmed before
    // adding this), so JsonStringEnumConverter changes nothing for GeekAPI's current consumers;
    // camelCase matches ASP.NET's existing System.Text.Json default, kept explicit for parity
    // with content-writer-v2's own (already-deployed) JSON config.
    .AddApplicationPart(typeof(ContentWriter.Api.Controllers.ProjectsController).Assembly)
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
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

var geekSeoUrl = (Environment.GetEnvironmentVariable("GEEK_SEO_API_URL") ?? "").Trim().TrimEnd('/');
builder.Services.AddHttpClient<GeekAPI.Services.ContentCreator.HttpGeekSeoNicheClient>(client =>
{
    if (!string.IsNullOrWhiteSpace(geekSeoUrl))
    {
        client.BaseAddress = new Uri(geekSeoUrl + "/");
        client.Timeout = TimeSpan.FromMinutes(2);
    }
});

var imageGeneratorBaseUrl =
    Environment.GetEnvironmentVariable("IMAGE_GENERATOR_BASE_URL")
    ?? "https://image-generator.geekatyourspot.com";
builder.Services.AddHttpClient<GeekAPI.Services.Gcw.HttpImageGeneratorClient>(client =>
{
    client.BaseAddress = new Uri(imageGeneratorBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(3);
});

// Content Writer V3: Services
// Provider-selectable generation: keyed registrations resolved via IContentGeneratorFactory,
// same pattern content-writer-v2's IContentProviderFactory uses. OpenAi reuses content-writer-v2's
// already-bound LlmProvidersOptions (registered by AddContentWriter below) rather than a separate key.
builder.Services.AddKeyedScoped<IContentGenerator, ClaudeContentGenerator>(ContentGeneratorProvider.Anthropic);
builder.Services.AddKeyedScoped<IContentGenerator, OpenAiContentGenerator>(ContentGeneratorProvider.OpenAi);
builder.Services.AddScoped<IContentGeneratorFactory, ContentGeneratorFactory>();
builder.Services.AddScoped<IAnalyticsAdapter, GoogleAnalyticsAdapter>();
builder.Services.AddScoped<IPublishAdapter, WordPressPublishAdapter>();
builder.Services.AddScoped<INotificationService, NotificationService>();

builder.Services.AddScoped<DepartmentContentService>();
builder.Services.AddGeekSa2Read();
builder.Services.AddScoped<SiteAnalyzer2SiteProfileReader>();

// content-writer-v2 merge (Phase 1): persistence reuses the "GeekRepository" named HttpClient
// already configured above (X-Repo-Key already attached) — no new credential, no direct call to
// GeekRepository from anywhere content-writer-v2's own code runs standalone. See
// GeekBackend/AGENTS.md § "Service topology & trust boundaries".
// Cross-department tool content cache reuses the same client/credential.
builder.Services.AddContentWriter(builder.Configuration,
    sp => new GeekRepositoryPersistenceStore(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<GeekRepositoryPersistenceStore>>()),
    sp => new GeekRepositoryToolContentCacheStore(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<GeekRepositoryToolContentCacheStore>>()));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();

app.Logger.LogInformation("CORS origins: {Origins}", string.Join(", ", corsOrigins));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (app.Environment.IsProduction())
    app.UseHttpsRedirection();

app.UseCors();
app.UseMiddleware<LegacyAuthRetiredMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

// content-writer-v2 merge (Phase 1): loads persisted projects/clients from GeekRepository at
// startup, same as content-writer-v2 does standalone.
await app.HydrateContentWriterAsync();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

public partial class Program;
