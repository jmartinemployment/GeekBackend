using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GeekBackend.Data.Data;

public class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = ReadEnvVar("DATABASE_URL")
            ?? ReadEnvVar("DIRECT_URL")
            ?? throw new InvalidOperationException("Neither DATABASE_URL nor DIRECT_URL found in .env. Add .env to the solution root.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options);
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
