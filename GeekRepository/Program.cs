using DotNetEnv;
using GeekRepository;
using GeekSeo.Persistence.Data;
using GeekRepository.Data;
using GeekRepository.Auth;
using GeekRepository.Extensions;
using GeekRepository.Infrastructure;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
var startupLogger = LoggerFactory.Create(logging => logging.AddSimpleConsole()).CreateLogger("Startup");

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

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
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

// Standalone context for WebPost content — entirely separate from AppDbContext/geek_blog,
// maps only to public.web_posts. Same physical database, isolated schema/table.
builder.Services.AddDbContext<GeekRepository.Data.ContentWriterDbContext>(options => options
    .UseNpgsql(connectionString)
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

// ContentWriterV3: Templates and Documents
builder.Services.AddDbContext<GeekRepository.Data.ContentWriterV3DbContext>(options => options
    .UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable(
            "content_writer_v3_ef_migrations_history",
            "content_writer_v3"))
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

// ContentWriterV4: standalone Jasper-style generation product — new schema, shares no code with V3
builder.Services.AddDbContext<GeekRepository.Data.ContentWriterV4DbContext>(options => options
    .UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable(
            "content_writer_v4_ef_migrations_history",
            "content_writer_v4"))
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

// ContentWriterV2: generic JSON-blob persistence store for the separate .NET content-writer-v2
// product's own IPersistenceStore — one table, arbitrary caller-chosen collections.
builder.Services.AddDbContext<GeekRepository.Data.ContentWriterV2DbContext>(options => options
    .UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable(
            "content_writer_v2_ef_migrations_history",
            "content_writer_v2"))
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddGeekRepository(connectionString);
builder.Services.AddGeekRepositoryAuth();
builder.Services.AddHostedService<SqlMigrationRunner>();

var app = builder.Build();

await ApplyPendingMigrationsAsync(app, startupLogger);
await ApplyContentWriterMigrationsAsync(app, startupLogger);
await ApplyContentWriterV3MigrationsAsync(app, startupLogger);
await ApplyContentWriterV4MigrationsAsync(app, startupLogger);
await ApplyContentWriterV2MigrationsAsync(app, startupLogger);
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

static async Task ApplyContentWriterMigrationsAsync(WebApplication app, ILogger logger)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GeekRepository.Data.ContentWriterDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("ContentWriter (public.web_posts) EF migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed applying ContentWriter EF migrations. Continuing startup.");
    }
}

static async Task ApplyContentWriterV3MigrationsAsync(WebApplication app, ILogger logger)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GeekRepository.Data.ContentWriterV3DbContext>();
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("ContentWriterV3 (content_writer_v3 schema) EF migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed applying ContentWriterV3 EF migrations. Continuing startup.");
    }
}

static async Task ApplyContentWriterV4MigrationsAsync(WebApplication app, ILogger logger)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GeekRepository.Data.ContentWriterV4DbContext>();
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("ContentWriterV4 (content_writer_v4 schema) EF migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed applying ContentWriterV4 EF migrations. Continuing startup.");
    }
}

static async Task ApplyContentWriterV2MigrationsAsync(WebApplication app, ILogger logger)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GeekRepository.Data.ContentWriterV2DbContext>();
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("ContentWriterV2 (content_writer_v2 schema) EF migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed applying ContentWriterV2 EF migrations. Continuing startup.");
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
