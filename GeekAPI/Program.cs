using DotNetEnv;
using GeekAPI.Extensions;
using GeekAPI.HttpClients;
using GeekAPI.Middleware;
using GeekAPI.Services;
using GeekApplication.Interfaces;
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
builder.Services.AddControllers();

var corsOrigins = CorsOriginParser.GetAllowedOrigins();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(corsOrigins)
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
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

public partial class Program;
