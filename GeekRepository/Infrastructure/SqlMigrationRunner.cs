using System.Reflection;
using Dapper;
using Npgsql;

namespace GeekRepository.Infrastructure;

public sealed class SqlMigrationRunner : IHostedService
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SqlMigrationRunner> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public SqlMigrationRunner(
        IDbConnectionFactory db,
        ILogger<SqlMigrationRunner> logger,
        IHostApplicationLifetime lifetime)
    {
        _db = db;
        _logger = logger;
        _lifetime = lifetime;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ApplyPendingScriptsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Auth SQL migration failed — stopping startup.");
            _lifetime.StopApplication();
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ApplyPendingScriptsAsync(CancellationToken cancellationToken)
    {
        using var conn = _db.CreateConnection();
        if (conn is NpgsqlConnection npgsql)
            await npgsql.OpenAsync(cancellationToken);
        else
            conn.Open();

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id SERIAL PRIMARY KEY,
                script_name TEXT NOT NULL UNIQUE,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """);

        var applied = (await conn.QueryAsync<string>(
            "SELECT script_name FROM schema_migrations ORDER BY script_name")).ToHashSet(StringComparer.Ordinal);

        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? AppContext.BaseDirectory;
        var sqlDir = Path.Combine(assemblyDir, "Migrations", "Sql");
        if (!Directory.Exists(sqlDir))
        {
            sqlDir = Path.Combine(AppContext.BaseDirectory, "Migrations", "Sql");
        }

        if (!Directory.Exists(sqlDir))
        {
            _logger.LogWarning("SQL migration directory not found at {Path}", sqlDir);
            return;
        }

        var scripts = Directory.GetFiles(sqlDir, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(static f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        foreach (var scriptPath in scripts)
        {
            var scriptName = Path.GetFileName(scriptPath);
            if (applied.Contains(scriptName))
                continue;

            var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);
            _logger.LogInformation("Applying SQL migration {Script}", scriptName);

            if (conn is NpgsqlConnection npg)
            {
                await using var tx = await npg.BeginTransactionAsync(cancellationToken);
                try
                {
                    await npg.ExecuteAsync(sql);
                    await npg.ExecuteAsync(
                        "INSERT INTO schema_migrations (script_name) VALUES (@scriptName)",
                        new { scriptName },
                        tx);
                    await tx.CommitAsync(cancellationToken);
                }
                catch
                {
                    await tx.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            else
            {
                using var tx = conn.BeginTransaction();
                try
                {
                    conn.Execute(sql, transaction: tx);
                    conn.Execute(
                        "INSERT INTO schema_migrations (script_name) VALUES (@scriptName)",
                        new { scriptName },
                        tx);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }
    }
}
