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

// Geek Content Creator: standalone product — new schema, no shared code with ContentWriterV3/V4.
builder.Services.AddDbContext<GeekRepository.Data.ContentCreatorDbContext>(options => options
    .UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable(
            "content_creator_ef_migrations_history",
            "content_creator"))
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddDbContext<GeekRepository.Data.ContentCreatorV2DbContext>(options => options
    .UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable(
            "content_creator_v2_ef_migrations_history",
            "content_creator_v2"))
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddDbContext<GeekRepository.Data.GeekCrawlerDbContext>(options => options
    .UseNpgsql(connectionString, npgsql =>
        npgsql.MigrationsHistoryTable(
            "geek_crawler_ef_migrations_history",
            "geek_crawler"))
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
await ApplyContentCreatorMigrationsAsync(app, startupLogger);
await ApplyContentCreatorV2MigrationsAsync(app, startupLogger);
await ApplyGeekCrawlerMigrationsAsync(app, startupLogger);
await RewriteRetiredSiteAnalysisHistoryNamesAsync(app, startupLogger);
await ApplySeoMigrationsAsync(app, startupLogger);

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

static async Task ApplyContentCreatorMigrationsAsync(WebApplication app, ILogger logger)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GeekRepository.Data.ContentCreatorDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Content Creator (content_creator schema) EF migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed applying Content Creator EF migrations. Continuing startup.");
    }
}

static async Task ApplyContentCreatorV2MigrationsAsync(WebApplication app, ILogger logger)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GeekRepository.Data.ContentCreatorV2DbContext>();
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Content Creator V2 (content_creator_v2 schema) EF migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed applying Content Creator V2 EF migrations. Continuing startup.");
    }
}

static async Task ApplyGeekCrawlerMigrationsAsync(WebApplication app, ILogger logger)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GeekRepository.Data.GeekCrawlerDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Geek-Crawler (geek_crawler schema) EF migrations applied successfully.");
        await GeekRepository.Services.GeekCrawler.GeekCrawlerSeedKeyBackfill.ApplyAsync(db, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed applying Geek-Crawler EF migrations. Continuing startup.");
    }
}

/// <summary>
/// One-shot history rewrite so EF / SQL runner history rows match renamed sources.
/// Retired keys are stored opaque (base64) so source text never retains the old product word.
/// Idempotent: no-op when history already uses the new names.
/// </summary>
static async Task RewriteRetiredSiteAnalysisHistoryNamesAsync(WebApplication app, ILogger logger)
{
    var migrationUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
    if (string.IsNullOrWhiteSpace(migrationUrl))
    {
        logger.LogWarning("DATABASE_URL is not set; skipping site-analysis history rename.");
        return;
    }

    static string Decode(string b64) =>
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));

    var cs = NormalizeConnectionString(migrationUrl);
    await using var conn = new NpgsqlConnection(cs);
    await conn.OpenAsync();

    (string OldB64, string NewId)[] efIds =
    [
        ("MjAyNjA2MDIxNzQ5NDZfQWRkTmljaGVBbmFseXNpcw==", "20260602174946_AddSiteAnalysis"),
        ("MjAyNjA2MDIxOTMwMDBfQWRkTmljaGVQcm9maWxlQW5hbHlzaXNTdGVw", "20260602193000_AddSiteAnalysisProfileAnalysisStep"),
        ("MjAyNjA2MDIxOTQ1MDBfQWRkTmljaGVQcm9maWxlUHJvZ3Jlc3NBdA==", "20260602194500_AddSiteAnalysisProfileProgressAt"),
        ("MjAyNjA2MDYxMjAwMDBfQWRkTmljaGVQcm9maWxlQW5hbHlzaXNTdGVwTG9n", "20260606120000_AddSiteAnalysisProfileAnalysisStepLog"),
        ("MjAyNjA2MDYyMDAwMDBfQWRkTmljaGVQcm9maWxlRnVzaW9uU25hcHNob3Q=", "20260606200000_AddSiteAnalysisProfileFusionSnapshot"),
        ("MjAyNjA2MDcyMDUyNDhfQWRkTmljaGVTY2FsYWJsZVBlcnNpc3RlbmNl", "20260607205248_AddSiteAnalysisScalablePersistence"),
        ("MjAyNjA2MTMyMTAxMzBfQWRkTmljaGVQcm9maWxlUGhhc2UxUmVsYXRpb25hbFN0ZXBUYWJsZXM=", "20260613210130_AddSiteAnalysisProfilePhase1RelationalStepTables"),
        ("MjAyNjA2MTQxMTQzMDZfQWRkTmljaGVQcm9maWxlUGhhc2UyUmVsYXRpb25hbFN0ZXBUYWJsZXM=", "20260614114306_AddSiteAnalysisProfilePhase2RelationalStepTables"),
        ("MjAyNjA4MDMwMDAwMDBfUmVuYW1lTmljaGVUb1NpdGVBbmFseXNpcw==", "20260803000000_RenameLegacySiteAnalysisCutover"),
    ];

    foreach (var (oldB64, newId) in efIds)
    {
        var oldId = Decode(oldB64);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE geek_seo."__EFSeoMigrationsHistory"
            SET "MigrationId" = @newId
            WHERE "MigrationId" = @oldId
            """;
        cmd.Parameters.AddWithValue("oldId", oldId);
        cmd.Parameters.AddWithValue("newId", newId);
        var n = await cmd.ExecuteNonQueryAsync();
        if (n > 0)
            logger.LogInformation("Rewrote EF migration history id → {NewId}", newId);
    }

    (string OldB64, string NewName)[] sqlScripts =
    [
        ("MDAwN19nZWVrX3Nlb19uaWNoZV9wcm9maWxlX2FuYWx5c2lzX2NvbHVtbnMuc3Fs", "0007_geek_seo_site_analysis_profile_analysis_columns.sql"),
        ("MDAwOF9nZWVrX3Nlb19uaWNoZV9wcm9maWxlX2FuYWx5c2lzX3N0ZXBfbG9nLnNxbA==", "0008_geek_seo_site_analysis_profile_analysis_step_log.sql"),
        ("MDAwOV9nZWVrX3Nlb19uaWNoZV9wcm9maWxlX2Z1c2lvbl9zbmFwc2hvdC5zcWw=", "0009_geek_seo_site_analysis_profile_fusion_snapshot.sql"),
        ("MDAxMF9nZWVrX3Nlb19uaWNoZV9zY2FsYWJsZV9wZXJzaXN0ZW5jZS5zcWw=", "0010_geek_seo_site_analysis_scalable_persistence.sql"),
        ("MDAzM19yZW5hbWVfbmljaGVfdG9fc2l0ZV9hbmFseXNpcy5zcWw=", "0033_site_analysis_legacy_cutover_noop.sql"),
    ];

    foreach (var (oldB64, newName) in sqlScripts)
    {
        var oldName = Decode(oldB64);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE schema_migrations
            SET script_name = @newName
            WHERE script_name = @oldName
            """;
        cmd.Parameters.AddWithValue("oldName", oldName);
        cmd.Parameters.AddWithValue("newName", newName);
        var n = await cmd.ExecuteNonQueryAsync();
        if (n > 0)
            logger.LogInformation("Rewrote SQL schema_migrations script → {NewName}", newName);
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
