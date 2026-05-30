using DotNetEnv;
using GeekRepository;
using EFCore.NamingConventions;
using GeekSeo.Persistence.Data;
using GeekRepository.Data;
using GeekRepository.Auth;
using GeekRepository.Extensions;
using GeekRepository.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
var startupLogger = LoggerFactory.Create(logging => logging.AddSimpleConsole()).CreateLogger("Startup");

builder.Services.AddControllers();

var rawDatabaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrWhiteSpace(rawDatabaseUrl))
    startupLogger.LogWarning("DATABASE_URL is not set. Repository service will start, but all data operations will fail.");

var connectionString = NormalizeConnectionString(rawDatabaseUrl ?? string.Empty);

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString)
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

var seoConnectionString = NormalizeConnectionString(
    Environment.GetEnvironmentVariable("GEEK_SEO_DATABASE_URL") ?? rawDatabaseUrl ?? string.Empty);

builder.Services.AddDbContext<SeoDbContext>(options => options
    .UseNpgsql(seoConnectionString, npgsql =>
        npgsql.MigrationsHistoryTable(
            SeoDbContextOptionsExtensions.MigrationsHistoryTableName,
            SeoDbContextOptionsExtensions.SchemaName))
    .UseSnakeCaseNamingConvention()
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddGeekRepository(connectionString);
builder.Services.AddGeekRepositoryAuth();
builder.Services.AddHostedService<SqlMigrationRunner>();

var app = builder.Build();

await ApplyPendingMigrationsAsync(app, startupLogger);
await ApplySeoMigrationsAsync(app, startupLogger);

app.UseMiddleware<GeekRepository.Middleware.LegacyAuthRetiredMiddleware>();
app.UseGeekRepositoryAuth();
app.MapControllers()
    .RequireAuthorization(RepositoryAuthConstants.InternalServicePolicy);

var port = Environment.GetEnvironmentVariable("PORT") ?? "5050";
app.Run($"http://0.0.0.0:{port}");

static async Task ApplyPendingMigrationsAsync(WebApplication app, ILogger logger)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Platform EF migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed applying platform EF migrations. Continuing startup.");
    }
}

static async Task ApplySeoMigrationsAsync(WebApplication app, ILogger logger)
{
    var migrationUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (string.IsNullOrWhiteSpace(migrationUrl))
    {
        logger.LogWarning("DATABASE_URL is not set; skipping Geek SEO schema migrations.");
        return;
    }

    var optionsBuilder = new DbContextOptionsBuilder<SeoDbContext>();
    optionsBuilder
        .UseGeekSeoDatabaseMigrations(NormalizeConnectionString(migrationUrl))
        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

    await using var db = new SeoDbContext(optionsBuilder.Options);
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Geek SEO (geek_seo) schema migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed applying Geek SEO migrations.");
        if (!app.Environment.IsDevelopment())
            throw;
    }
}

static string NormalizeConnectionString(string rawValue)
{
    var value = rawValue.ReplaceLineEndings("").Trim().Trim('"', '\'');
    if (!value.Contains("://", StringComparison.Ordinal))
        return value;
    try
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var databaseUri))
            return value;
        if (databaseUri.Scheme != "postgres" && databaseUri.Scheme != "postgresql")
            return value;
        var userInfo = databaseUri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = databaseUri.AbsolutePath.Trim('/').Split('/', 2)[0];
        var query = System.Web.HttpUtility.ParseQueryString(databaseUri.Query);
        var sslMode = query["sslmode"] ?? query["ssl_mode"];
        var connBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = databaseUri.Host,
            Port = databaseUri.Port > 0 ? databaseUri.Port : 5432,
            Username = username,
            Password = password,
            Database = database,
        };
        if (!string.IsNullOrWhiteSpace(sslMode) && Enum.TryParse<SslMode>(sslMode, true, out var parsedMode))
            connBuilder.SslMode = parsedMode;
        return connBuilder.ConnectionString;
    }
    catch
    {
        return value;
    }
}

public partial class Program;
