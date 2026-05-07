using DotNetEnv;
using EFCore.NamingConventions;
using GeekBackend.Api.Middleware;
using GeekBackend.Api.Services;
using GeekRepository.Data;
using GeekRepository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var connectionString = (Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL environment variable is not set. Add it to .env locally or as a Railway variable in production."))
    .ReplaceLineEndings("").Trim();


builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString)
    .UseSnakeCaseNamingConventions()
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddScoped<IDepartmentRepository, DepartmentRepository>();
builder.Services.AddScoped<ICaseStudyRepository, CaseStudyRepository>();
builder.Services.AddScoped<IUseCaseRepository, UseCaseRepository>();
builder.Services.AddScoped<DepartmentContentService>();

// Auth repositories
builder.Services.AddScoped<IOidcStorageRepository, OidcStorageRepository>();
builder.Services.AddScoped<IUserAuthRepository, UserAuthRepository>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IOAuthTokenRepository, OAuthTokenRepository>();
builder.Services.AddScoped<IOAuthClientRepository, OAuthClientRepository>();
builder.Services.AddScoped<IPendingVerificationRepository, PendingVerificationRepository>();
builder.Services.AddScoped<IAuditRepository, AuditRepository>();
builder.Services.AddScoped<IRbacRepository, RbacRepository>();

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
