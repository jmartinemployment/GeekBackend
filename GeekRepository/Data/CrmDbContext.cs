using GeekRepository.Data.Entities.Crm;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

/// <summary>
/// The geek_crm schema (docs/GEEK-CRM-DATA-MODEL.md), served to GeekAPI through repo/crm/*.
/// No EF migrations: the schema is created by Migrations/Sql/0050_geek_crm_schema.sql through
/// SqlMigrationRunner, like geek_social. Every column is the snake_case of its property.
/// The SIC lookup exists in the schema but is not mapped yet.
/// </summary>
public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
    public const string Schema = "geek_crm";

    public DbSet<CrmTenantRow> Tenants => Set<CrmTenantRow>();
    public DbSet<CrmParticipantRow> Participants => Set<CrmParticipantRow>();
    public DbSet<CrmPersonRow> Persons => Set<CrmPersonRow>();
    public DbSet<CrmOrganizationRow> Organizations => Set<CrmOrganizationRow>();
    public DbSet<CrmContactPointRow> ContactPoints => Set<CrmContactPointRow>();
    public DbSet<CrmParticipantLinkRow> Links => Set<CrmParticipantLinkRow>();
    public DbSet<CrmPipelineRow> Pipelines => Set<CrmPipelineRow>();
    public DbSet<CrmStageRow> Stages => Set<CrmStageRow>();
    public DbSet<CrmDealRow> Deals => Set<CrmDealRow>();
    public DbSet<CrmDealStageHistoryRow> DealStageHistory => Set<CrmDealStageHistoryRow>();
    public DbSet<CrmDealParticipantRow> DealParticipants => Set<CrmDealParticipantRow>();
    public DbSet<CrmActivityRow> Activities => Set<CrmActivityRow>();
    public DbSet<CrmPropertyDefinitionRow> PropertyDefinitions => Set<CrmPropertyDefinitionRow>();
    public DbSet<CrmPropertyValueRow> PropertyValues => Set<CrmPropertyValueRow>();
    public DbSet<CrmNaicsCodeRow> NaicsCodes => Set<CrmNaicsCodeRow>();
    public DbSet<CrmProductRow> Products => Set<CrmProductRow>();
    public DbSet<CrmProductPriceRow> ProductPrices => Set<CrmProductPriceRow>();
    public DbSet<CrmPricingRulesRow> PricingRules => Set<CrmPricingRulesRow>();
    public DbSet<CrmPricingUpliftTierRow> PricingUpliftTiers => Set<CrmPricingUpliftTierRow>();
    public DbSet<CrmDealLineItemRow> DealLineItems => Set<CrmDealLineItemRow>();
    public DbSet<CrmCustomerHoldingRow> CustomerHoldings => Set<CrmCustomerHoldingRow>();
    public DbSet<CrmTenantSubscriptionRow> TenantSubscriptions => Set<CrmTenantSubscriptionRow>();
    public DbSet<CrmProjectRow> Projects => Set<CrmProjectRow>();
    public DbSet<CrmTenantUserRow> TenantUsers => Set<CrmTenantUserRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CrmTenantRow>(e => { e.ToTable("tenants", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmParticipantRow>(e => { e.ToTable("participants", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmPersonRow>(e => { e.ToTable("persons", Schema); e.HasKey(x => x.ParticipantId); });
        modelBuilder.Entity<CrmOrganizationRow>(e => { e.ToTable("organizations", Schema); e.HasKey(x => x.ParticipantId); });
        modelBuilder.Entity<CrmContactPointRow>(e => { e.ToTable("contact_points", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmParticipantLinkRow>(e => { e.ToTable("participant_links", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmPipelineRow>(e => { e.ToTable("pipelines", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmStageRow>(e => { e.ToTable("stages", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmDealRow>(e =>
        {
            e.ToTable("deals", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Qualification).HasColumnType("jsonb");
        });
        modelBuilder.Entity<CrmDealStageHistoryRow>(e => { e.ToTable("deal_stage_history", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmDealParticipantRow>(e => { e.ToTable("deal_participants", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmActivityRow>(e => { e.ToTable("activities", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmPropertyDefinitionRow>(e =>
        {
            e.ToTable("property_definitions", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Options).HasColumnType("jsonb");
        });
        modelBuilder.Entity<CrmPropertyValueRow>(e =>
        {
            e.ToTable("property_values", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Value).HasColumnType("jsonb");
        });
        modelBuilder.Entity<CrmNaicsCodeRow>(e => { e.ToTable("naics_codes", Schema); e.HasKey(x => x.Code); });
        modelBuilder.Entity<CrmProductRow>(e => { e.ToTable("products", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmProductPriceRow>(e => { e.ToTable("product_prices", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmPricingRulesRow>(e => { e.ToTable("pricing_rules", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmPricingUpliftTierRow>(e => { e.ToTable("pricing_uplift_tiers", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmDealLineItemRow>(e =>
        {
            e.ToTable("deal_line_items", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.EstimateInputs).HasColumnType("jsonb");
        });
        modelBuilder.Entity<CrmProjectRow>(e => { e.ToTable("projects", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmTenantUserRow>(e => { e.ToTable("tenant_users", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmCustomerHoldingRow>(e => { e.ToTable("customer_holdings", Schema); e.HasKey(x => x.Id); });
        modelBuilder.Entity<CrmTenantSubscriptionRow>(e => { e.ToTable("tenant_subscriptions", Schema); e.HasKey(x => x.Id); });

        // Foreign keys without navigations: they exist so EF inserts a parent before its children
        // (a participant before its contact points, a deal before its stage history). The
        // database owns deletes (ON DELETE in 0050), so EF never cascades on its own.
        Fk<CrmParticipantRow, CrmTenantRow>(modelBuilder, x => x.TenantId);
        Fk<CrmPersonRow, CrmParticipantRow>(modelBuilder, x => x.ParticipantId);
        Fk<CrmOrganizationRow, CrmParticipantRow>(modelBuilder, x => x.ParticipantId);
        Fk<CrmContactPointRow, CrmParticipantRow>(modelBuilder, x => x.ParticipantId);
        Fk<CrmParticipantLinkRow, CrmParticipantRow>(modelBuilder, x => x.FromParticipantId);
        Fk<CrmParticipantLinkRow, CrmParticipantRow>(modelBuilder, x => x.ToParticipantId);
        Fk<CrmPipelineRow, CrmTenantRow>(modelBuilder, x => x.TenantId);
        Fk<CrmStageRow, CrmPipelineRow>(modelBuilder, x => x.PipelineId);
        Fk<CrmDealRow, CrmPipelineRow>(modelBuilder, x => x.PipelineId);
        Fk<CrmDealRow, CrmStageRow>(modelBuilder, x => x.StageId);
        Fk<CrmDealRow, CrmParticipantRow>(modelBuilder, x => x.OrganizationParticipantId);
        Fk<CrmDealStageHistoryRow, CrmDealRow>(modelBuilder, x => x.DealId);
        Fk<CrmDealStageHistoryRow, CrmStageRow>(modelBuilder, x => x.FromStageId);
        Fk<CrmDealStageHistoryRow, CrmStageRow>(modelBuilder, x => x.ToStageId);
        Fk<CrmDealParticipantRow, CrmDealRow>(modelBuilder, x => x.DealId);
        Fk<CrmDealParticipantRow, CrmParticipantRow>(modelBuilder, x => x.ParticipantId);
        Fk<CrmActivityRow, CrmParticipantRow>(modelBuilder, x => x.ParticipantId);
        Fk<CrmActivityRow, CrmDealRow>(modelBuilder, x => x.DealId);
        Fk<CrmPropertyDefinitionRow, CrmTenantRow>(modelBuilder, x => x.TenantId);
        Fk<CrmPropertyValueRow, CrmPropertyDefinitionRow>(modelBuilder, x => x.DefinitionId);
        Fk<CrmProductRow, CrmTenantRow>(modelBuilder, x => x.TenantId);
        Fk<CrmProductRow, CrmParticipantRow>(modelBuilder, x => x.PartnerParticipantId);
        Fk<CrmProductPriceRow, CrmProductRow>(modelBuilder, x => x.ProductId);
        Fk<CrmPricingRulesRow, CrmTenantRow>(modelBuilder, x => x.TenantId);
        Fk<CrmPricingUpliftTierRow, CrmTenantRow>(modelBuilder, x => x.TenantId);
        Fk<CrmDealLineItemRow, CrmDealRow>(modelBuilder, x => x.DealId);
        Fk<CrmDealLineItemRow, CrmProductRow>(modelBuilder, x => x.ProductId);
        Fk<CrmCustomerHoldingRow, CrmParticipantRow>(modelBuilder, x => x.ParticipantId);
        Fk<CrmCustomerHoldingRow, CrmProductRow>(modelBuilder, x => x.ProductId);
        Fk<CrmCustomerHoldingRow, CrmDealRow>(modelBuilder, x => x.DealId);
        Fk<CrmCustomerHoldingRow, CrmDealLineItemRow>(modelBuilder, x => x.DealLineItemId);
        Fk<CrmTenantSubscriptionRow, CrmTenantRow>(modelBuilder, x => x.SubscriberTenantId);
        Fk<CrmProjectRow, CrmTenantRow>(modelBuilder, x => x.TenantId);
        Fk<CrmTenantUserRow, CrmTenantRow>(modelBuilder, x => x.TenantId);
        Fk<CrmProjectRow, CrmParticipantRow>(modelBuilder, x => x.ParticipantId);
        Fk<CrmProjectRow, CrmDealRow>(modelBuilder, x => x.DealId);
        Fk<CrmProjectRow, CrmCustomerHoldingRow>(modelBuilder, x => x.CustomerHoldingId);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(SocialDbContext.ToSnakeCase(property.Name));
        }
    }

    private static void Fk<TChild, TParent>(ModelBuilder modelBuilder, System.Linq.Expressions.Expression<Func<TChild, object?>> key)
        where TChild : class where TParent : class =>
        modelBuilder.Entity<TChild>().HasOne<TParent>().WithMany().HasForeignKey(key).OnDelete(DeleteBehavior.NoAction);
}
