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
using Npgsql;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
var startupLogger = LoggerFactory.Create(logging => logging.AddSimpleConsole())
    .CreateLogger("Startup");

builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var rawDatabaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrWhiteSpace(rawDatabaseUrl))
{
    startupLogger.LogWarning("DATABASE_URL is not set. API will start, but database-backed endpoints will fail until DATABASE_URL is configured.");
}

var connectionString = NormalizeConnectionString(rawDatabaseUrl ?? string.Empty);


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
        logger.LogError(ex, "Failed applying database migrations at startup. Continuing startup without auto-migration.");
    }
}

static string NormalizeConnectionString(string rawValue)
{
    var value = rawValue.ReplaceLineEndings("").Trim().Trim('"', '\'');

    if (!value.Contains("://", StringComparison.Ordinal))
    {
        return value;
    }

    try
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var databaseUri))
        {
            return value;
        }

        if (databaseUri.Scheme != "postgres" && databaseUri.Scheme != "postgresql")
        {
            return value;
        }

        var userInfo = databaseUri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = databaseUri.AbsolutePath.Trim('/').Split('/', 2)[0];
        var query = System.Web.HttpUtility.ParseQueryString(databaseUri.Query);
        var sslMode = query["sslmode"] ?? query["ssl_mode"];

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = databaseUri.Host,
            Port = databaseUri.Port > 0 ? databaseUri.Port : 5432,
            Username = username,
            Password = password,
            Database = database
        };

        if (!string.IsNullOrWhiteSpace(sslMode) && Enum.TryParse<SslMode>(sslMode, true, out var parsedMode))
        {
            builder.SslMode = parsedMode;
        }

        return builder.ConnectionString;
    }
    catch
    {
        // Fall back to the provided value so startup does not fail.
        return value;
    }
}
