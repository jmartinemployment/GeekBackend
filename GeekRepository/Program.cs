using DotNetEnv;
using GeekRepository;
using GeekRepository.Data;
using GeekRepository.Middleware;
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

builder.Services.AddGeekRepository(connectionString);

var app = builder.Build();

await ApplyPendingMigrationsAsync(app, startupLogger);

app.UseMiddleware<RepoApiKeyMiddleware>();
app.MapControllers();

var port = Environment.GetEnvironmentVariable("PORT") ?? "5050";
app.Run($"http://0.0.0.0:{port}");

static async Task ApplyPendingMigrationsAsync(WebApplication app, ILogger logger)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed applying database migrations. Continuing startup.");
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
            Database = database
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
