using DotNetEnv;
using GeekAPI.Middleware;
using GeekAPI.Services;
using GeekApplication.Interfaces;
using GeekRepository;
using GeekRepository.Data;
using GeekRepository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

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
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

// Content repositories
builder.Services.AddScoped<IDepartmentRepository, DepartmentRepository>();
builder.Services.AddScoped<ICaseStudyRepository, CaseStudyRepository>();
builder.Services.AddScoped<IUseCaseRepository, UseCaseRepository>();
builder.Services.AddScoped<DepartmentContentService>();

builder.Services.AddGeekRepository(connectionString);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}

await ApplyPendingMigrationsAsync(app);

app.UseMiddleware<ApiKeyMiddleware>();
app.UseCors();
app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");

static async Task ApplyPendingMigrationsAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("StartupMigrations");

    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed applying database migrations at startup.");
        throw;
    }
}
