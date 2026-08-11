using System.Text.Json.Serialization;
using DotNetEnv;
using GeekAPI.Auth;
using GeekAPI.Controllers;
using GeekAPI.Extensions;
using GeekAPI.HttpClients;
using GeekAPI.Middleware;
using GeekAPI.Services;
using GeekAPI.Services.ContentWriterV3;
using GeekAPI.Services.SiteAnalyzer2;
using GeekAPI.Services.Workflow.Hosting;
using GeekAPI.Services.Workflow.Infrastructure;
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
    ?? "https://image-generator.geekatyourspot.com";
builder.Services.AddHttpClient<GeekAPI.Services.Gcw.HttpImageGeneratorClient>(client =>
{
    client.BaseAddress = new Uri(imageGeneratorBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(3);
});

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

// Workflow: loads persisted projects/clients from GeekRepository at startup.
await app.HydrateWorkflowAsync();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

public partial class Program;
