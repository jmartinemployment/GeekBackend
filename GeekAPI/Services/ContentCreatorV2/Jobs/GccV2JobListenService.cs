using Npgsql;

namespace GeekAPI.Services.ContentCreatorV2.Jobs;

/// <summary>
/// Optional cross-instance wake: if <c>GCC_V2_LISTEN_DATABASE_URL</c> (or <c>DATABASE_URL</c>) is
/// configured, opens a dedicated connection and <c>LISTEN gcc_v2_job</c>. <see cref="NpgsqlConnection.WaitAsync"/>
/// blocks until a notification arrives (or the connection breaks) — this is event-driven, not a
/// polling loop. When no DB URL is configured this hosted service simply does nothing; the
/// in-process <see cref="GccV2JobWake"/> Channel alone is sufficient for a single instance.
/// </summary>
public sealed class GccV2JobListenService : BackgroundService
{
    private const string Channel = "gcc_v2_job";

    private readonly GccV2JobWake _wake;
    private readonly ILogger<GccV2JobListenService> _logger;

    public GccV2JobListenService(GccV2JobWake wake, ILogger<GccV2JobListenService> logger)
    {
        _wake = wake;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = ResolveConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogInformation(
                "GCC_V2_LISTEN_DATABASE_URL/DATABASE_URL not set — skipping Postgres LISTEN; " +
                "relying on in-process Channel wake only (fine for a single GeekAPI instance).");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(stoppingToken);

                conn.Notification += (_, e) =>
                {
                    if (Guid.TryParse(e.Payload, out var jobId))
                        _wake.Wake(jobId);
                };

                await using (var cmd = new NpgsqlCommand($"LISTEN {Channel};", conn))
                {
                    await cmd.ExecuteNonQueryAsync(stoppingToken);
                }

                _logger.LogInformation("GccV2JobListenService: listening for NOTIFY {Channel}.", Channel);

                // Blocks (no polling) until a notification is delivered or the connection drops.
                while (!stoppingToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GccV2JobListenService connection failed; retrying in 5s.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static string? ResolveConnectionString()
    {
        var raw = Environment.GetEnvironmentVariable("GCC_V2_LISTEN_DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        return string.IsNullOrWhiteSpace(raw) ? null : NormalizeConnectionString(raw);
    }

    /// <summary>Accepts either a raw Npgsql connection string or a <c>postgres://</c> URL.</summary>
    private static string NormalizeConnectionString(string rawValue)
    {
        var value = rawValue.ReplaceLineEndings("").Trim().Trim('"', '\'');
        if (!value.Contains("://", StringComparison.Ordinal))
            return value;

        try
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var databaseUri))
                return value;
            if (databaseUri.Scheme is not ("postgres" or "postgresql"))
                return value;

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
                Database = database,
            };
            if (!string.IsNullOrWhiteSpace(sslMode) && Enum.TryParse<SslMode>(sslMode, true, out var parsedMode))
                builder.SslMode = parsedMode;

            return builder.ConnectionString;
        }
        catch
        {
            return value;
        }
    }
}
