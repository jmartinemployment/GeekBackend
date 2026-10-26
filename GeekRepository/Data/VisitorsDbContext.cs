using System.Text;
using GeekRepository.Data.Entities.Visitors;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

/// <summary>
/// The geek_visitors schema, owned by GeekRepository: geekatyourspot.com's visit records, served
/// to GeekAPI through repo/visitors/*. Mongo is the crawl store only; a product store is a schema
/// in this service's own database (AGENTS.md, Jeff 2026-10-26).
/// </summary>
/// <remarks>
/// No EF migrations and no MigrationsHistoryTable: the schema is created and maintained by
/// Migrations/Sql/0042_geek_visitors_schema.sql through SqlMigrationRunner, the same way geek_gtm
/// and geek_blog are. Every column is the snake_case of its property name, applied once below, so
/// the script and the row types can be read against each other.
/// </remarks>
public sealed class VisitorsDbContext(DbContextOptions<VisitorsDbContext> options) : DbContext(options)
{
    public const string Schema = "geek_visitors";

    public DbSet<VisitorRow> Visitors => Set<VisitorRow>();
    public DbSet<VisitorSessionRow> Sessions => Set<VisitorSessionRow>();
    public DbSet<VisitorPageRow> Pages => Set<VisitorPageRow>();
    public DbSet<VisitorClickRow> Clicks => Set<VisitorClickRow>();
    public DbSet<VisitorDropRow> Drops => Set<VisitorDropRow>();
    public DbSet<VisitorDailyReportRow> DailyReports => Set<VisitorDailyReportRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VisitorRow>(e =>
        {
            e.ToTable("visitors", Schema);
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.LastSeenAt).HasDatabaseName("ix_visitors_last_seen");
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<VisitorSessionRow>(e =>
        {
            e.ToTable("sessions", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.RowNo).ValueGeneratedOnAdd().UseIdentityAlwaysColumn();
            e.HasIndex(x => x.RowNo).IsUnique().HasDatabaseName("ix_sessions_row_no");
            e.HasIndex(x => new { x.StartedAt, x.RowNo }).HasDatabaseName("ix_sessions_started");
            e.HasIndex(x => new { x.VisitorId, x.StartedAt }).HasDatabaseName("ix_sessions_visitor_started");
            e.HasIndex(x => x.LastSeenAt).HasDatabaseName("ix_sessions_last_seen");
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasMany(x => x.Pages)
                .WithOne()
                .HasForeignKey(p => p.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VisitorPageRow>(e =>
        {
            e.ToTable("session_pages", Schema);
            e.HasKey(x => new { x.SessionId, x.PageViewId });
            e.HasMany(x => x.Clicks)
                .WithOne()
                .HasForeignKey(c => new { c.SessionId, c.PageViewId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VisitorClickRow>(e =>
        {
            e.ToTable("page_clicks", Schema);
            e.HasKey(x => new { x.SessionId, x.PageViewId, x.Seq });
        });

        modelBuilder.Entity<VisitorDropRow>(e =>
        {
            e.ToTable("filter_day_drops", Schema);
            e.HasKey(x => new { x.Day, x.Reason });
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<VisitorDailyReportRow>(e =>
        {
            e.ToTable("daily_reports", Schema);
            e.HasKey(x => x.Day);
        });

        // One rule for every column name: the snake_case of the property, as the script writes it.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    /// <summary>"GeoPostalCode" to "geo_postal_code", "UaRaw" to "ua_raw", "LiFatId" to "li_fat_id".</summary>
    public static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
