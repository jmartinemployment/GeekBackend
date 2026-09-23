using GeekAPI.Services.ContentCreatorV2.Write;
using System.Text.Json.Serialization;
using DotNetEnv;
using GeekAPI.Auth;
using GeekAPI.Controllers;
using GeekAPI.Controllers.ContentCreatorV2.Auth;
using GeekAPI.Controllers.ContentCreatorV2.Hubs;
using GeekAPI.Controllers.GeekCrawler.Hubs;
using GeekAPI.Controllers.Workflow.Hubs;
using GeekAPI.Extensions;
using GeekAPI.HttpClients;
using GeekAPI.Middleware;
using GeekAPI.Services;
using GeekAPI.Services.ContentCreatorV2;
using GeekAPI.Services.ContentWriterV3;
using GeekAPI.Services.GeekCrawler;
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

// Structured logs so the host can read a severity.
//
// The default console logger writes human-readable text to stdout, and Railway classifies by
// stream: stdout is info, stderr is error. So LogError(ex, ...) landed as an info line and never
// appeared under an error filter -- unhandled exceptions were being reported by the app and made
// invisible by the transport. JSON console emits the level as a field, which Railway parses.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
});

builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior =
        BackgroundServiceExceptionBehavior.Ignore;
});

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
// X-Repo-Key is the only credential GeekRepository accepts, so an unset REPO_API_KEY is a
// misconfiguration rather than a mode. This used to attach the header only when the key was
// non-empty, which meant an empty key produced a client that called GeekRepository with no
// credential at all — failing open on the tier that holds the data, and only at the first request.
var repoApiKey = Environment.GetEnvironmentVariable("REPO_API_KEY");
if (string.IsNullOrWhiteSpace(repoApiKey))
{
    throw new InvalidOperationException(
        "REPO_API_KEY is not set. It is the only credential GeekRepository accepts, and a client "
        + "built without it would call GeekRepository unauthenticated.");
}

var repositoryClientBuilder = builder.Services.AddHttpClient("GeekRepository", client =>
{
    client.BaseAddress = new Uri(repoUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
});
repositoryClientBuilder.ConfigureHttpClient(client =>
    client.DefaultRequestHeaders.Add("X-Repo-Key", repoApiKey));

builder.Services.AddScoped<ICaseStudyRepository, HttpCaseStudyRepository>();
builder.Services.AddScoped<IDepartmentRepository, HttpDepartmentRepository>();
builder.Services.AddScoped<IUseCaseRepository, HttpUseCaseRepository>();
builder.Services.AddScoped<IBlogRepository, HttpBlogRepository>();
builder.Services.AddScoped<IGlossaryRepository, HttpGlossaryRepository>();
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
// The same scoped instance, seen through the narrow interface grounding depends on.
builder.Services.AddScoped<GeekAPI.HttpClients.IGccProjectReader>(sp =>
    sp.GetRequiredService<GeekAPI.HttpClients.HttpGccRepository>());
builder.Services.AddScoped<GeekAPI.Services.ContentCreator.GccGenerateService>();
builder.Services.AddScoped<GeekAPI.Services.ContentCreator.GccGroundingResolver>();
builder.Services.AddScoped<GeekAPI.Services.ContentCreator.GccGenerationCoordinator>();
// One prompt set per content type, resolved by registry -- see
// content-creator-v2/plans/prompts-per-content-type.md. Pillar, Blog and Tool only for now.
builder.Services.AddScoped<GeekAPI.Services.ContentCreator.ContentTypes.IContentTypePrompts,
    GeekAPI.Services.ContentCreator.ContentTypes.PillarPrompts>();
builder.Services.AddScoped<GeekAPI.Services.ContentCreator.ContentTypes.IContentTypePrompts,
    GeekAPI.Services.ContentCreator.ContentTypes.BlogPrompts>();
builder.Services.AddScoped<GeekAPI.Services.ContentCreator.ContentTypes.IContentTypePrompts,
    GeekAPI.Services.ContentCreator.ContentTypes.ToolPrompts>();
builder.Services.AddScoped<GeekAPI.Services.ContentCreator.ContentTypes.IContentTypePromptRegistry,
    GeekAPI.Services.ContentCreator.ContentTypes.ContentTypePromptRegistry>();
builder.Services.AddSingleton<GeekAPI.Services.ContentCreator.GccGenerateNotifier>();
builder.Services.AddSingleton<GeekAPI.Services.ContentCreator.GccGenerateJobRunner>();
builder.Services.AddScoped<GeekAPI.Services.ContentCreator.GccArtifactExportService>();
builder.Services.AddScoped<GeekAPI.Services.ContentCreator.GccCompetitorAnalysisResolver>();
// In-process job tracking for GccController's generate endpoints — a ConcurrentDictionary with no
// constructor dependencies, and never actually registered. GccController has therefore been
// unconstructable since GccJobStore was added to its constructor: every action on it, not only the
// ones that touch jobs, throws "Unable to resolve service for type GccJobStore" at the DI
// activation step, before any action code runs. That went unnoticed while nothing routed through
// GccController in a way anyone was exercising; consolidating the client routes onto it (this
// session, to fix a route-ambiguity 500) is what put a live path through it and surfaced this as a
// second 500 in production. AddSingleton to match ToolsGenerationJobStore, the sibling job store
// this comment on it already calls "same shape... one GeekAPI instance only": in-memory job state
// has to survive across requests within the process, so scoped or transient would silently lose it.
builder.Services.AddSingleton<GeekAPI.Services.ContentCreator.GccJobStore>();
builder.Services.AddContentCreatorV2(builder.Configuration);
builder.Services.AddGeekCrawler(builder.Configuration, builder.Environment);
builder.Services.AddScoped<GeekAPI.Services.ContentCreatorV2.Write.GccV2CreateLibraryWriter>();

// GeekOAuth-issued JWT bearer. Originally added only so the v2 realtime hub could require
// [Authorize] (ApiKeyMiddleware's header-based auth can't run over a WebSocket upgrade) — routes
// reached through ApiKeyMiddleware still authenticate exactly as they did before this existed.
// It is no longer only that: every Content Creator project, client, task, time and deliverable
// route now authenticates through this same scheme via content-creator.manage, ahead of
// ApiKeyMiddleware in the pipeline (UseAuthorization() runs first and short-circuits on failure).
//
// Was read only for the v2 hub, and registration of the JWT scheme plus every policy on it —
// content-creator.manage included — lived inside `if (!string.IsNullOrWhiteSpace(...))`. That
// coupling stopped being correct the moment ManagePolicy became the thing guarding real project,
// client and billing routes rather than an auxiliary hub feature: if this variable were ever
// unset, the hub would degrade (the old comment said so), but content-creator.manage would not
// exist as a registered policy at all, and every route carrying [Authorize(Policy = ManagePolicy)]
// would throw InvalidOperationException — "policy not found" — on first request. That is a 500
// that looks like an unrelated crash, not a clean refusal, and would have shipped silently until
// someone hit it.
//
// Required now, the same as REPO_API_KEY above: GeekAPI refuses to start without it rather than
// exposing billing-adjacent routes a policy that does not exist.
var gccV2HubAuthority = (Environment.GetEnvironmentVariable("GEEK_OAUTH_AUTHORITY")
    ?? Environment.GetEnvironmentVariable("AUTH_SERVER_URL")
    ?? string.Empty).Trim().TrimEnd('/');
if (string.IsNullOrWhiteSpace(gccV2HubAuthority))
{
    throw new InvalidOperationException(
        "GEEK_OAUTH_AUTHORITY (or AUTH_SERVER_URL) is not set. It authenticates every Content "
        + "Creator project, client, task, time and deliverable route via content-creator.manage, "
        + "and GeekAPI will not start without an authority to validate those tokens against.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = gccV2HubAuthority;
        options.RequireHttpsMetadata = !gccV2HubAuthority.Contains("localhost", StringComparison.OrdinalIgnoreCase);
        // Inbound claims keep the names the token uses. Mapped, "sub" arrives renamed to the
        // long WS-Federation nameidentifier URI — which made NameClaimType = "sub" above find
        // nothing — and "scope" is not reliably carried through either. Every reader in this
        // service already looks for both spellings (ClaimsExtensions, CurrentUserContext, the
        // hub user-id providers), so turning mapping off costs nothing and makes the scope
        // claim readable by the policy below.
        options.MapInboundClaims = false;
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

builder.Services.AddAuthorizationBuilder()
    // Content Creator's project, client and billing rows. GeekOAuth is shared across Geek
    // apps, so holding a valid token is not evidence of anything in particular — the scope is
    // what says this caller was granted this data.
    //
    // The claim arrives as one space-delimited string ("openid profile content-creator.manage"),
    // so an exact-match RequireClaim would never match it. Split, then look for the member.
    .AddPolicy(GeekAPI.Auth.ContentCreatorAuthConstants.ManagePolicy, policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            context.User.Claims.Any(static claim =>
                (claim.Type is "scope" or "scp")
                && claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(GeekAPI.Auth.ContentCreatorAuthConstants.ManageScope, StringComparer.Ordinal)));
    });

builder.Services.AddHttpClient("GccV2GoogleApis", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient("GccV2MicrosoftGraph", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddSingleton<GeekAPI.Services.ContentCreatorV2.Gsc.GccV2GscSearchAnalyticsClient>();
builder.Services.AddSingleton<GeekAPI.Services.ContentCreatorV2.Gsc.GccV2GscOAuthStateStore>();
builder.Services.AddSingleton<GeekAPI.Services.ContentCreatorV2.Drive.GccV2DriveFilesClient>();
builder.Services.AddSingleton<GeekAPI.Services.ContentCreatorV2.Drive.GccV2DriveOAuthStateStore>();
builder.Services.AddSingleton<GeekAPI.Services.ContentCreatorV2.SharePoint.GccV2SharePointGraphClient>();
builder.Services.AddSingleton<GeekAPI.Services.ContentCreatorV2.SharePoint.GccV2SharePointOAuthStateStore>();

var geekSeoUrl = (Environment.GetEnvironmentVariable("GEEK_SEO_API_URL") ?? "").Trim().TrimEnd('/');
builder.Services.AddHttpClient<GeekAPI.Services.GeekSeo.HttpGeekSeoSiteAnalyzerClient>(client =>
{
    if (!string.IsNullOrWhiteSpace(geekSeoUrl))
    {
        client.BaseAddress = new Uri(geekSeoUrl + "/");
        client.Timeout = TimeSpan.FromMinutes(2);
    }
});

var geekCrawlerRagUrl = (Environment.GetEnvironmentVariable("GEEK_CRAWLER_RAG_URL") ?? "").Trim().TrimEnd('/');
var geekCrawlerRagApiKey = (Environment.GetEnvironmentVariable("GEEK_CRAWLER_RAG_API_KEY") ?? "").Trim();
builder.Services.AddHttpClient<GeekAPI.Services.GeekCrawler.IGeekCrawlerRagClient, GeekAPI.Services.GeekCrawler.HttpGeekCrawlerRagClient>(client =>
{
    if (!string.IsNullOrWhiteSpace(geekCrawlerRagUrl))
    {
        client.BaseAddress = new Uri(geekCrawlerRagUrl + "/");
        // Citeable generate (retrieve + o3 draft) can exceed 2m; keep index/query under same client.
        client.Timeout = TimeSpan.FromMinutes(6);
    }

    if (!string.IsNullOrWhiteSpace(geekCrawlerRagApiKey))
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", geekCrawlerRagApiKey);
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

// gccV2HubAuthority is guaranteed non-empty here — GeekAPI already refused to start otherwise —
// so this runs unconditionally. UseAuthorization() sits ahead of ApiKeyMiddleware: a request to a
// [Authorize(Policy = ManagePolicy)] route is accepted or refused here, before ApiKeyMiddleware
// gets a turn. Every route ApiKeyMiddleware still owns (nothing under this policy) keeps
// authenticating exactly as it always did.
app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<LegacyAuthRetiredMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

app.MapHub<GccV2RealtimeHub>("/hubs/gcc-v2-realtime");
app.MapHub<GeekCrawlerRealtimeHub>("/hubs/geek-crawler-realtime");
app.MapHub<WorkflowRealtimeHub>("/hubs/workflow-realtime");

// Workflow: loads persisted projects/clients from GeekRepository at startup.
await app.HydrateWorkflowAsync();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

public partial class Program;
