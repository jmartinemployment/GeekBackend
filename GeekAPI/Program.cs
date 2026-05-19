using DotNetEnv;
using GeekAPI.Extensions;
using GeekAPI.HttpClients;
using GeekAPI.Hubs;
using GeekAPI.Middleware;
using GeekAPI.Services;
using GeekApplication.Interfaces;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(
                "http://localhost:3000",
                "https://seo.geekatyourspot.com")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()));

builder.Services.AddGeekSeoApi(builder.Configuration);

var repoUrl = Environment.GetEnvironmentVariable("REPO_URL") ?? "http://localhost:5050";
var repoApiKey = Environment.GetEnvironmentVariable("REPO_API_KEY") ?? string.Empty;
builder.Services.AddHttpClient("GeekRepository", client =>
{
    client.BaseAddress = new Uri(repoUrl);
    if (!string.IsNullOrWhiteSpace(repoApiKey))
        client.DefaultRequestHeaders.Add("X-Repo-Key", repoApiKey);
});

// Auth repositories — HTTP-backed, calling GeekRepository
builder.Services.AddScoped<IUserRepository, HttpUserRepository>();
builder.Services.AddScoped<IDeviceOauthRepository, HttpDeviceRepository>();
builder.Services.AddScoped<IOAuthTokenRepository, HttpOAuthTokenRepository>();
builder.Services.AddScoped<IOAuthClientRepository, HttpOAuthClientRepository>();
builder.Services.AddScoped<IOidcStorageRepository, HttpOidcStorageRepository>();
builder.Services.AddScoped<IAuditRepository, HttpAuditRepository>();
builder.Services.AddScoped<IPendingVerificationRepository, HttpPendingVerificationRepository>();

// Content repositories — HTTP-backed, calling GeekRepository
builder.Services.AddScoped<ICaseStudyRepository, HttpCaseStudyRepository>();
builder.Services.AddScoped<IDepartmentRepository, HttpDepartmentRepository>();
builder.Services.AddScoped<IUseCaseRepository, HttpUseCaseRepository>();

builder.Services.AddScoped<DepartmentContentService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}

app.UseCors();
var jwtConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JWT_KEY"))
    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTH_SERVER_URL"));
if (jwtConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
}
else
{
    app.UseMiddleware<DevUserMiddleware>();
}
app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<SeoFeatureGateMiddleware>();
app.UseMiddleware<SeoUsageGateMiddleware>();
app.MapControllers();
app.MapHub<SeoContentScoringHub>("/hubs/seo-scoring");

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
