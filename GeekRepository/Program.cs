using DotNetEnv;
using GeekRepository;
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
        // BsonValue must be written as plain JSON. Reflecting over it hits typed accessors that
        // throw, which kills serialisation after a 200 and a partial body are already on the wire.
        options.JsonSerializerOptions.Converters.Add(new GeekRepository.Serialization.BsonValueJsonConverter());
    });

var rawDatabaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrWhiteSpace(rawDatabaseUrl))
    startupLogger.LogWarning("DATABASE_URL is not set. Repository service will start, but all data operations will fail.");

var connectionString = NormalizeConnectionString(rawDatabaseUrl ?? string.Empty);

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(connectionString)
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

// No SeoDbContext, and no GEEK_SEO_DATABASE_URL. Geek-SEO was never an authorized service
// (Jeff, 2026-09-29) and remains on disk only as a source to copy from later, never to be
// project-referenced or called from here -- the same "copy, never reuse" rule AGENTS.md already
// applies to the content-writer repos. Its 27 controllers, 26 repositories, SeoDataRegistration
// and the GeekSa2Read library went with it.

// Google Tag Manager account store (geek_gtm). Its own product, its own schema -- it used to
// borrow SeoDbContext and the geek_seo schema, which is why it broke when the unauthorized
// Geek-SEO layer was deleted. No MigrationsHistoryTable: geek_gtm is created and maintained by
// Migrations/Sql/0041_geek_gtm_schema.sql, the same way geek_blog is.
builder.Services.AddDbContext<GeekRepository.Data.GtmDbContext>(options => options
    .UseNpgsql(connectionString)
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

builder.Services.AddGeekRepository(connectionString);
builder.Services.AddGeekRepositoryAuth();
builder.Services.AddExceptionHandler<GeekRepository.Infrastructure.DbUpdateConcurrencyExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHostedService<SqlMigrationRunner>();

var app = builder.Build();

await ApplyPendingMigrationsAsync(app, startupLogger);
await ApplyContentWriterMigrationsAsync(app, startupLogger);
await ApplyContentWriterV3MigrationsAsync(app, startupLogger);
await ApplyContentWriterV4MigrationsAsync(app, startupLogger);
await ApplyContentWriterV2MigrationsAsync(app, startupLogger);
await ApplyContentCreatorMigrationsAsync(app, startupLogger);
await ApplyContentCreatorV2MigrationsAsync(app, startupLogger);
await EnsureGeekCrawlerMongoIndexesAsync(app, startupLogger);

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (DbUpdateConcurrencyException)
    {
        throw; // handled by DbUpdateConcurrencyExceptionHandler → 409
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

/// <summary>
/// Applies the content_creator migrations, and stops the service if they do not apply.
/// </summary>
/// <remarks>
/// The other Apply* methods here log and continue. That is not a neutral choice: on 2026-09-21 the
/// Geek-Crawler seed-key backfill was throwing 42703 on every boot, the failure was swallowed, and
/// the deployment reported SUCCESS — a schema silently not migrating behind a green deploy.
///
/// content_creator holds the project, client and billing rows, so it does not get that treatment.
/// A failure here means the tables the API is about to serve are not the tables it was built
/// against; serving anyway turns one loud startup error into an unbounded number of confusing
/// runtime ones. It rethrows, which fails the deploy and keeps the previous release running.
///
/// The other contexts are deliberately left alone: Geek-Crawler is failing today, so making them
/// all fatal would crash-loop this service rather than protect anything.
/// </remarks>
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
        logger.LogCritical(ex, "Failed applying Content Creator EF migrations — refusing to start.");
        throw;
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

static async Task EnsureGeekCrawlerMongoIndexesAsync(WebApplication app, ILogger logger)
{
    try
    {
        var mongo = app.Services.GetRequiredService<GeekRepository.Services.IMongoGeekCrawlerService>();
        await mongo.EnsureIndexesAsync();
        logger.LogInformation("Geek-Crawler Mongo indexes ensured.");
    }
    catch (Exception ex)
    {
        // Indexes are best-effort: Hostinger Mongo can be briefly unreachable (e.g. IPv6
        // from Railway). Do not crash the whole repository — crawler routes will fail
        // loudly until Mongo recovers; everything else must stay up.
        logger.LogError(ex, "Failed ensuring Geek-Crawler Mongo indexes. Continuing startup.");
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
