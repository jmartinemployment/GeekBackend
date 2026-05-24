extern alias GeekRepo;

using GeekApplication.Entities.OpenIddict;
using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories.OpenIddict;
using Npgsql;

namespace GeekBackend.IntegrationTests;

public sealed class OpenIddictRepositoryIntegrationTests
{
    [Fact]
    public async Task ApplicationRepository_CreateFindDelete_RoundTrip()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured)
            return;

        await ApplyMigrationsAsync();
        var factory = new NpgsqlConnectionFactory(IntegrationTestConfig.DatabaseUrl!);
        var repo = new DapperApplicationRepository(factory);
        var id = Guid.NewGuid().ToString("N");

        var created = await repo.CreateAsync(new GeekOpenIddictApplication
        {
            Id = id,
            ClientId = $"test-{id[..8]}",
            DisplayName = "Integration Test Client",
            ClientType = "confidential",
            Permissions = "[]"
        });

        Assert.True(created.IsSuccess, created.Error);

        var found = await repo.FindByClientIdAsync(created.Value!.ClientId!);
        Assert.True(found.IsSuccess);
        Assert.NotNull(found.Value);
        Assert.Equal(id, found.Value!.Id);

        var deleted = await repo.DeleteAsync(id);
        Assert.True(deleted.IsSuccess);
        Assert.True(deleted.Value);
    }

    private static async Task ApplyMigrationsAsync()
    {
        var sqlDir = Path.Combine(AppContext.BaseDirectory, "Migrations", "Sql");
        if (!Directory.Exists(sqlDir))
        {
            sqlDir = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "GeekRepository", "Migrations", "Sql"));
        }

        await using var conn = new NpgsqlConnection(IntegrationTestConfig.DatabaseUrl);
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
