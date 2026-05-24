using Npgsql;

namespace GeekBackend.IntegrationTests;

internal static class SqlMigrationTestHelper
{
    public static async Task ApplyAsync()
    {
        var connectionString = IntegrationTestConfig.DatabaseUrl
            ?? throw new InvalidOperationException("TEST_DATABASE_URL is required.");

        var sqlDir = Path.Combine(AppContext.BaseDirectory, "Migrations", "Sql");
        if (!Directory.Exists(sqlDir))
        {
            sqlDir = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "GeekRepository", "Migrations", "Sql"));
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id SERIAL PRIMARY KEY,
                script_name TEXT NOT NULL UNIQUE,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using (var list = new NpgsqlCommand("SELECT script_name FROM schema_migrations", conn))
        await using (var reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                applied.Add(reader.GetString(0));
        }

        foreach (var scriptPath in Directory.GetFiles(sqlDir, "*.sql").OrderBy(static f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(scriptPath);
            if (applied.Contains(name))
                continue;

            var sql = await File.ReadAllTextAsync(scriptPath);
            await using var tx = await conn.BeginTransactionAsync();
            await using (var exec = new NpgsqlCommand(sql, conn, tx))
                await exec.ExecuteNonQueryAsync();
            await using (var mark = new NpgsqlCommand(
                "INSERT INTO schema_migrations (script_name) VALUES (@name)", conn, tx))
            {
                mark.Parameters.AddWithValue("name", name);
                await mark.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
    }
}
