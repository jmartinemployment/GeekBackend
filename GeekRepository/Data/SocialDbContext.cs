using System.Text;
using GeekRepository.Data.Entities.Social;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

/// <summary>
/// The geek_social schema, owned by GeekRepository: GeekSocial's accounts, posts, engagement events,
/// reply drafts and audit trail, served to GeekAPI through repo/social/*. A product store is a
/// schema in this service's own database (AGENTS.md, Jeff 2026-10-26).
/// </summary>
/// <remarks>
/// No EF migrations and no MigrationsHistoryTable: the schema is created and maintained by
/// Migrations/Sql/0043_geek_social_schema.sql through SqlMigrationRunner, the same way geek_gtm and
/// geek_visitors are. Every column is the snake_case of its property name, applied once below, so
/// the script and the row types can be read against each other.
/// </remarks>
public sealed class SocialDbContext(DbContextOptions<SocialDbContext> options) : DbContext(options)
{
    public const string Schema = "geek_social";

    public DbSet<SocialAccountRow> Accounts => Set<SocialAccountRow>();
    public DbSet<SocialPostRow> Posts => Set<SocialPostRow>();
    public DbSet<EngagementEventRow> Events => Set<EngagementEventRow>();
    public DbSet<ReplyDraftRow> Drafts => Set<ReplyDraftRow>();
    public DbSet<ActionAuditRow> Audit => Set<ActionAuditRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SocialAccountRow>(e =>
        {
            e.ToTable("social_accounts", Schema);
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.PlatformAccountId }).IsUnique().HasDatabaseName("ux_social_accounts_platform_account");
            e.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<SocialPostRow>(e =>
        {
            e.ToTable("social_posts", Schema);
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AccountId, x.PlatformPostId }).IsUnique().HasDatabaseName("ux_social_posts_account_post");
        });

        modelBuilder.Entity<EngagementEventRow>(e =>
        {
            e.ToTable("engagement_events", Schema);
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Platform, x.PlatformEventId }).IsUnique().HasDatabaseName("ux_engagement_events_platform_event");
        });

        modelBuilder.Entity<ReplyDraftRow>(e =>
        {
            e.ToTable("reply_drafts", Schema);
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<ActionAuditRow>(e =>
        {
            e.ToTable("action_audit", Schema);
            e.HasKey(x => x.Id);
        });

        // One rule for every column name: the snake_case of the property, as the script writes it.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    /// <summary>"PlatformAccountId" to "platform_account_id", "TokenIv" to "token_iv".</summary>
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
