using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

/// <summary>Google Tag Manager OAuth identity keyed by friendly alias per Geek user.</summary>
/// <remarks>
/// Copied from Geek-SEO's SeoGtmAccountConnection rather than referenced, per AGENTS.md
/// "copy, never reuse". The table it maps is created by Migrations/Sql/0041_geek_gtm_schema.sql,
/// which also relocated the rows out of the deleted geek_seo schema.
/// </remarks>
public sealed class GtmAccountConnection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string AccountKey { get; set; }
    public string? GoogleEmail { get; set; }
    public byte[] EncryptedRefreshToken { get; set; } = [];
    public byte[] EncryptionIv { get; set; } = [];
    public byte[] EncryptionTag { get; set; } = [];
    public DateTimeOffset ConnectedAt { get; set; }
}

/// <summary>
/// The geek_gtm schema, owned by GeekRepository. Serves Geek-GTM-MCP through GeekAPI's
/// api/gtm/internal/* proxy -- the MCP server never reaches Postgres itself.
/// </summary>
/// <remarks>
/// No EF migrations and no MigrationsHistoryTable: the schema is created and maintained by
/// Migrations/Sql/*.sql through SqlMigrationRunner, the same way geek_blog is. Column names are
/// quoted PascalCase because that is the shape the rows already had when they were relocated.
/// </remarks>
public sealed class GtmDbContext(DbContextOptions<GtmDbContext> options) : DbContext(options)
{
    public DbSet<GtmAccountConnection> GtmAccountConnections => Set<GtmAccountConnection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GtmAccountConnection>(e =>
        {
            e.ToTable("gtm_account_connections", "geek_gtm");
            e.HasKey(x => x.Id);
            e.Property(x => x.AccountKey).HasMaxLength(128);
            e.HasIndex(x => new { x.UserId, x.AccountKey }).IsUnique();
        });
    }
}
