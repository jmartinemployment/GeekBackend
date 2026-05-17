using DotNetEnv;
using GeekAPI.HttpClients;
using GeekAPI.Middleware;
using GeekAPI.Services;
using GeekApplication.Interfaces;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

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

app.UseMiddleware<ApiKeyMiddleware>();
app.UseCors();
app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");
