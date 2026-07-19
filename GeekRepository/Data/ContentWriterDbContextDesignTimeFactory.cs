using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GeekRepository.Data;

public class ContentWriterDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ContentWriterDbContext>
{
    public ContentWriterDbContext CreateDbContext(string[] args)
    {
        var connectionString = ReadEnvVar("DATABASE_URL")
            ?? ReadEnvVar("DIRECT_URL")
            ?? throw new InvalidOperationException("Neither DATABASE_URL nor DIRECT_URL found in .env. Add .env to the solution root.");

        var options = new DbContextOptionsBuilder<ContentWriterDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new ContentWriterDbContext(options);
    }

    private static readonly string[] EnvFileNames = [".env", ".env.local"];

    private static string? ReadEnvVar(string name)
    {
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 4; i++)
        {
            foreach (var fileName in EnvFileNames)
            {
                var envFile = Path.Combine(dir, fileName);
                if (!File.Exists(envFile)) continue;

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
