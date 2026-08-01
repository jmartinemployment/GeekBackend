using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GeekRepository.Data;

public class ContentCreatorDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ContentCreatorDbContext>
{
    public ContentCreatorDbContext CreateDbContext(string[] args)
    {
        var connectionString = ReadEnvVar("DATABASE_URL")
            ?? ReadEnvVar("DIRECT_URL")
            ?? "Host=localhost;Database=geek_design_time;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ContentCreatorDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable(
                    "content_creator_ef_migrations_history",
                    "content_creator"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new ContentCreatorDbContext(options);
    }

    private static string? ReadEnvVar(string name)
    {
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 4; i++)
        {
            var envFile = Path.Combine(dir, ".env");
            if (File.Exists(envFile))
            {
                foreach (var line in File.ReadAllLines(envFile))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;
                    var eq = trimmed.IndexOf('=');
                    var key = trimmed[..eq].Trim();
                    if (key != name) continue;
                    return trimmed[(eq + 1)..].Trim().Trim('"');
                }
            }
            dir = Directory.GetParent(dir)?.FullName ?? dir;
        }
        return null;
    }
}
