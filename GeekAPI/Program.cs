using System.Threading.RateLimiting;
using DotNetEnv;
using GeekAPI.Extensions;
using GeekAPI.HttpClients;
using GeekAPI.Hubs;
using GeekAPI.Middleware;
using GeekAPI.Services;
using GeekAPI.Workers;
using GeekApplication.Interfaces;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

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
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddSignalR();

var corsOrigins = CorsOriginParser.GetAllowedOrigins();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(corsOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()));

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("token", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15)
            }));

    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15)
            }));

    options.AddPolicy("twofactor", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(5)
            }));

    options.AddPolicy("device-challenge", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15)
            }));
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("devices", policy =>
        policy.RequireAssertion(context =>
            context.User.Claims.Any(c =>
                c.Type is OpenIddictConstants.Claims.Scope or "scope"
                && c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("devices.manage", StringComparer.Ordinal))));

    options.AddPolicy("sync", policy =>
        policy.RequireAssertion(context =>
            context.User.Claims.Any(c =>
                c.Type is OpenIddictConstants.Claims.Scope or "scope"
                && c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("sync.read", StringComparer.Ordinal))));

    options.AddPolicy("mcp", policy =>
        policy.RequireAssertion(context =>
            context.User.Claims.Any(c =>
                c.Type is OpenIddictConstants.Claims.Scope or "scope"
                && c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("mcp.tools", StringComparer.Ordinal))));
});

builder.Services.AddGeekOpenIddict(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<GeekAPI.Infrastructure.OpenIddictScopeSeeder>();
builder.Services.AddHostedService<GeekAPI.Infrastructure.OpenIddictClientSeeder>();
builder.Services.AddHostedService<JtiCleanupWorker>();

var repoUrl = Environment.GetEnvironmentVariable("REPO_URL") ?? "http://localhost:5050";
var repoApiKey = Environment.GetEnvironmentVariable("REPO_API_KEY") ?? string.Empty;
builder.Services.AddSingleton<RepositoryAccessTokenProvider>();
builder.Services.AddTransient<RepositoryBearerTokenHandler>();
builder.Services.AddHttpClient("GeekRepositoryToken")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15));
var repositoryClientBuilder = builder.Services.AddHttpClient("GeekRepository", client =>
    client.BaseAddress = new Uri(repoUrl));
if (!string.IsNullOrWhiteSpace(repoApiKey) && builder.Environment.IsDevelopment())
{
    repositoryClientBuilder.ConfigureHttpClient(client =>
        client.DefaultRequestHeaders.Add("X-Repo-Key", repoApiKey));
}
else
{
    repositoryClientBuilder.AddHttpMessageHandler<RepositoryBearerTokenHandler>();
}

builder.Services.AddScoped<IUserRepository, HttpUserRepository>();
builder.Services.AddScoped<IUserSecretsRepository, HttpUserSecretsRepository>();
builder.Services.AddScoped<IDeviceOauthRepository, HttpDeviceRepository>();
builder.Services.AddScoped<IOAuthTokenRepository, HttpOAuthTokenRepository>();
builder.Services.AddScoped<IOAuthClientRepository, HttpOAuthClientRepository>();
builder.Services.AddScoped<IOidcStorageRepository, HttpOidcStorageRepository>();
builder.Services.AddScoped<IAuditRepository, HttpAuditRepository>();
builder.Services.AddScoped<IPendingVerificationRepository, HttpPendingVerificationRepository>();
builder.Services.AddScoped<ISyncRepository, HttpSyncRepository>();

builder.Services.AddScoped<ICaseStudyRepository, HttpCaseStudyRepository>();
builder.Services.AddScoped<IDepartmentRepository, HttpDepartmentRepository>();
builder.Services.AddScoped<IUseCaseRepository, HttpUseCaseRepository>();

builder.Services.AddScoped<DepartmentContentService>();

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<JtiRevocationMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();
app.MapRazorPages();
app.MapHub<SyncHub>("/hubs/sync");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

public partial class Program;
