using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeekRepository.Data;

public sealed class SeoDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SeoDbContext>
{
    public SeoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("GEEK_SEO_DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Database=postgres;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<SeoDbContext>()
            .UseGeekSeoDatabaseMigrations(connectionString)
            .Options;

        return new SeoDbContext(options);
    }
}
