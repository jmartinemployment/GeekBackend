using System;
using System.Collections.Generic;
using GeekBackend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Data.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Account> Accounts { get; set; }

    public virtual DbSet<AiUsageLog> AiUsageLogs { get; set; }

    public virtual DbSet<Analytic> Analytics { get; set; }

    public virtual DbSet<Article> Articles { get; set; }

    public virtual DbSet<Audience> Audiences { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<AuditLog1> AuditLogs1 { get; set; }

    public virtual DbSet<AuditLogEntry> AuditLogEntries { get; set; }

    public virtual DbSet<BrandVoice> BrandVoices { get; set; }

    public virtual DbSet<BreakType> BreakTypes { get; set; }

    public virtual DbSet<Bucket> Buckets { get; set; }

    public virtual DbSet<BucketsAnalytic> BucketsAnalytics { get; set; }

    public virtual DbSet<BucketsVector> BucketsVectors { get; set; }

    public virtual DbSet<Campaign> Campaigns { get; set; }

    public virtual DbSet<Campaign1> Campaigns1 { get; set; }

    public virtual DbSet<CampaignAsset> CampaignAssets { get; set; }

    public virtual DbSet<CampaignPerformance> CampaignPerformances { get; set; }

    public virtual DbSet<CateringActivity> CateringActivities { get; set; }

    public virtual DbSet<CateringCapacitySetting> CateringCapacitySettings { get; set; }

    public virtual DbSet<CateringEvent> CateringEvents { get; set; }

    public virtual DbSet<CateringPackageTemplate> CateringPackageTemplates { get; set; }

    public virtual DbSet<CateringProposalToken> CateringProposalTokens { get; set; }

    public virtual DbSet<CheckDiscount> CheckDiscounts { get; set; }

    public virtual DbSet<CheckItem> CheckItems { get; set; }

    public virtual DbSet<CheckItemModifier> CheckItemModifiers { get; set; }

    public virtual DbSet<CheckVoidedItem> CheckVoidedItems { get; set; }

    public virtual DbSet<CircuitReset> CircuitResets { get; set; }

    public virtual DbSet<Combo> Combos { get; set; }

    public virtual DbSet<CommissionRule> CommissionRules { get; set; }

    public virtual DbSet<ComplianceAlert> ComplianceAlerts { get; set; }

    public virtual DbSet<ContentCalendar> ContentCalendars { get; set; }

    public virtual DbSet<ContentTemplate> ContentTemplates { get; set; }

    public virtual DbSet<CustomOauthProvider> CustomOauthProviders { get; set; }

    public virtual DbSet<Customer> Customers { get; set; }

    public virtual DbSet<CustomerFeedback> CustomerFeedbacks { get; set; }

    public virtual DbSet<CycleCount> CycleCounts { get; set; }

    public virtual DbSet<CycleCountItem> CycleCountItems { get; set; }

    public virtual DbSet<Device> Devices { get; set; }

    public virtual DbSet<Device1> Devices1 { get; set; }

    public virtual DbSet<DeviceMode> DeviceModes { get; set; }

    public virtual DbSet<Event> Events { get; set; }

    public virtual DbSet<FlowState> FlowStates { get; set; }

    public virtual DbSet<FoodCostRecipe> FoodCostRecipes { get; set; }

    public virtual DbSet<FoodCostRecipeIngredient> FoodCostRecipeIngredients { get; set; }

    public virtual DbSet<GiftCard> GiftCards { get; set; }

    public virtual DbSet<GiftCardRedemption> GiftCardRedemptions { get; set; }

    public virtual DbSet<HouseAccount> HouseAccounts { get; set; }

    public virtual DbSet<Identity> Identities { get; set; }

    public virtual DbSet<Instance> Instances { get; set; }

    public virtual DbSet<InventoryItem> InventoryItems { get; set; }

    public virtual DbSet<InventoryLog> InventoryLogs { get; set; }

    public virtual DbSet<Invoice> Invoices { get; set; }

    public virtual DbSet<InvoiceLineItem> InvoiceLineItems { get; set; }

    public virtual DbSet<Keyword> Keywords { get; set; }

    public virtual DbSet<KioskProfile> KioskProfiles { get; set; }

    public virtual DbSet<KnowledgeBase> KnowledgeBases { get; set; }

    public virtual DbSet<LaborTarget> LaborTargets { get; set; }

    public virtual DbSet<Layaway> Layaways { get; set; }

    public virtual DbSet<LocationGroup> LocationGroups { get; set; }

    public virtual DbSet<LocationGroupMember> LocationGroupMembers { get; set; }

    public virtual DbSet<LoyaltyReward> LoyaltyRewards { get; set; }

    public virtual DbSet<LoyaltyTransaction> LoyaltyTransactions { get; set; }

    public virtual DbSet<MarketingAutomation> MarketingAutomations { get; set; }

    public virtual DbSet<MarketplaceIntegration> MarketplaceIntegrations { get; set; }

    public virtual DbSet<MarketplaceMenuMapping> MarketplaceMenuMappings { get; set; }

    public virtual DbSet<MarketplaceOrder> MarketplaceOrders { get; set; }

    public virtual DbSet<MarketplaceStatusSyncJob> MarketplaceStatusSyncJobs { get; set; }

    public virtual DbSet<MarketplaceWebhookEvent> MarketplaceWebhookEvents { get; set; }

    public virtual DbSet<MenuCategory> MenuCategories { get; set; }

    public virtual DbSet<MenuItem> MenuItems { get; set; }

    public virtual DbSet<MenuItemModifierGroup> MenuItemModifierGroups { get; set; }

    public virtual DbSet<MenuSyncLog> MenuSyncLogs { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<MessageTemplate> MessageTemplates { get; set; }

    public virtual DbSet<MessageThread> MessageThreads { get; set; }

    public virtual DbSet<MfaAmrClaim> MfaAmrClaims { get; set; }

    public virtual DbSet<MfaChallenge> MfaChallenges { get; set; }

    public virtual DbSet<MfaFactor> MfaFactors { get; set; }

    public virtual DbSet<MfaSecret> MfaSecrets { get; set; }

    public virtual DbSet<MfaTrustedDevice> MfaTrustedDevices { get; set; }

    public virtual DbSet<Migration> Migrations { get; set; }

    public virtual DbSet<Modifier> Modifiers { get; set; }

    public virtual DbSet<ModifierGroup> ModifierGroups { get; set; }

    public virtual DbSet<OauthAuthorization> OauthAuthorizations { get; set; }

    public virtual DbSet<OauthClient> OauthClients { get; set; }

    public virtual DbSet<OauthClient1> OauthClients1 { get; set; }

    public virtual DbSet<OauthClientState> OauthClientStates { get; set; }

    public virtual DbSet<OauthConsent> OauthConsents { get; set; }

    public virtual DbSet<OauthToken> OauthTokens { get; set; }

    public virtual DbSet<Object> Objects { get; set; }

    public virtual DbSet<OidcStorage> OidcStorages { get; set; }

    public virtual DbSet<OneTimeToken> OneTimeTokens { get; set; }

    public virtual DbSet<Order> Orders { get; set; }

    public virtual DbSet<OrderCheck> OrderChecks { get; set; }

    public virtual DbSet<OrderItem> OrderItems { get; set; }

    public virtual DbSet<OrderItemModifier> OrderItemModifiers { get; set; }

    public virtual DbSet<OrderSentiment> OrderSentiments { get; set; }

    public virtual DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }

    public virtual DbSet<OrderTemplate> OrderTemplates { get; set; }

    public virtual DbSet<PasswordHistory> PasswordHistories { get; set; }

    public virtual DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

    public virtual DbSet<PayrollPeriod> PayrollPeriods { get; set; }

    public virtual DbSet<PendingVerification> PendingVerifications { get; set; }

    public virtual DbSet<PendingVerification1> PendingVerifications1 { get; set; }

    public virtual DbSet<PeripheralDevice> PeripheralDevices { get; set; }

    public virtual DbSet<Permission> Permissions { get; set; }

    public virtual DbSet<PermissionSet> PermissionSets { get; set; }

    public virtual DbSet<PrimaryCategory> PrimaryCategories { get; set; }

    public virtual DbSet<PrintJob> PrintJobs { get; set; }

    public virtual DbSet<Printer> Printers { get; set; }

    public virtual DbSet<PrinterProfile> PrinterProfiles { get; set; }

    public virtual DbSet<PrismaMigration> PrismaMigrations { get; set; }

    public virtual DbSet<PtoRequest> PtoRequests { get; set; }

    public virtual DbSet<PurchaseInvoice> PurchaseInvoices { get; set; }

    public virtual DbSet<PurchaseLineItem> PurchaseLineItems { get; set; }

    public virtual DbSet<RecipeIngredient> RecipeIngredients { get; set; }

    public virtual DbSet<RecurringReservation> RecurringReservations { get; set; }

    public virtual DbSet<ReferralConfig> ReferralConfigs { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<ReportSchedule> ReportSchedules { get; set; }

    public virtual DbSet<Reservation> Reservations { get; set; }

    public virtual DbSet<Restaurant> Restaurants { get; set; }

    public virtual DbSet<RestaurantAiCredential> RestaurantAiCredentials { get; set; }

    public virtual DbSet<RestaurantDeliveryCredential> RestaurantDeliveryCredentials { get; set; }

    public virtual DbSet<RestaurantGroup> RestaurantGroups { get; set; }

    public virtual DbSet<RestaurantLoyaltyConfig> RestaurantLoyaltyConfigs { get; set; }

    public virtual DbSet<RestaurantProviderProfile> RestaurantProviderProfiles { get; set; }

    public virtual DbSet<RestaurantProviderProfileEvent> RestaurantProviderProfileEvents { get; set; }

    public virtual DbSet<RestaurantSupplierCredential> RestaurantSupplierCredentials { get; set; }

    public virtual DbSet<RestaurantTable> RestaurantTables { get; set; }

    public virtual DbSet<RetailCategory> RetailCategories { get; set; }

    public virtual DbSet<RetailItem> RetailItems { get; set; }

    public virtual DbSet<RetailItemOptionSet> RetailItemOptionSets { get; set; }

    public virtual DbSet<RetailOption> RetailOptions { get; set; }

    public virtual DbSet<RetailOptionSet> RetailOptionSets { get; set; }

    public virtual DbSet<RetailQuickKey> RetailQuickKeys { get; set; }

    public virtual DbSet<RetailStock> RetailStocks { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<S3MultipartUpload> S3MultipartUploads { get; set; }

    public virtual DbSet<S3MultipartUploadsPart> S3MultipartUploadsParts { get; set; }

    public virtual DbSet<SamlProvider> SamlProviders { get; set; }

    public virtual DbSet<SamlRelayState> SamlRelayStates { get; set; }

    public virtual DbSet<SavedReport> SavedReports { get; set; }

    public virtual DbSet<ScheduleTemplate> ScheduleTemplates { get; set; }

    public virtual DbSet<SchemaMigration> SchemaMigrations { get; set; }

    public virtual DbSet<SchemaMigration1> SchemaMigrations1 { get; set; }

    public virtual DbSet<Session> Sessions { get; set; }

    public virtual DbSet<Session1> Sessions1 { get; set; }

    public virtual DbSet<Shift> Shifts { get; set; }

    public virtual DbSet<Site> Sites { get; set; }

    public virtual DbSet<SmartGroup> SmartGroups { get; set; }

    public virtual DbSet<SsoDomain> SsoDomains { get; set; }

    public virtual DbSet<SsoProvider> SsoProviders { get; set; }

    public virtual DbSet<StaffAvailability> StaffAvailabilities { get; set; }

    public virtual DbSet<StaffNotification> StaffNotifications { get; set; }

    public virtual DbSet<StaffPin> StaffPins { get; set; }

    public virtual DbSet<StaffTaxInfo> StaffTaxInfos { get; set; }

    public virtual DbSet<Station> Stations { get; set; }

    public virtual DbSet<StationCategoryMapping> StationCategoryMappings { get; set; }

    public virtual DbSet<Subscription> Subscriptions { get; set; }

    public virtual DbSet<Subscription1> Subscriptions1 { get; set; }

    public virtual DbSet<SwapRequest> SwapRequests { get; set; }

    public virtual DbSet<TaxJurisdiction> TaxJurisdictions { get; set; }

    public virtual DbSet<TeamMember> TeamMembers { get; set; }

    public virtual DbSet<TeamMemberJob> TeamMemberJobs { get; set; }

    public virtual DbSet<TemplateShift> TemplateShifts { get; set; }

    public virtual DbSet<TimeEntry> TimeEntries { get; set; }

    public virtual DbSet<TimecardEditRequest> TimecardEditRequests { get; set; }

    public virtual DbSet<UnitConversion> UnitConversions { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<User1> Users1 { get; set; }

    public virtual DbSet<User2> Users2 { get; set; }

    public virtual DbSet<UserGoogleCredential> UserGoogleCredentials { get; set; }

    public virtual DbSet<UserRestaurantAccess> UserRestaurantAccesses { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<UserSession> UserSessions { get; set; }

    public virtual DbSet<VectorIndex> VectorIndexes { get; set; }

    public virtual DbSet<Vendor> Vendors { get; set; }

    public virtual DbSet<VerificationToken> VerificationTokens { get; set; }

    public virtual DbSet<WebauthnChallenge> WebauthnChallenges { get; set; }

    public virtual DbSet<WebauthnCredential> WebauthnCredentials { get; set; }

    public virtual DbSet<WorkweekConfig> WorkweekConfigs { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseNpgsql("Host=db.mpnruwauxsqbrxvlksnf.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=2025NW18thStreet&33445;SSL Mode=Require");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum("auth", "aal_level", new[] { "aal1", "aal2", "aal3" })
            .HasPostgresEnum("auth", "code_challenge_method", new[] { "s256", "plain" })
            .HasPostgresEnum("auth", "factor_status", new[] { "unverified", "verified" })
            .HasPostgresEnum("auth", "factor_type", new[] { "totp", "webauthn", "phone" })
            .HasPostgresEnum("auth", "oauth_authorization_status", new[] { "pending", "approved", "denied", "expired" })
            .HasPostgresEnum("auth", "oauth_client_type", new[] { "public", "confidential" })
            .HasPostgresEnum("auth", "oauth_registration_type", new[] { "dynamic", "manual" })
            .HasPostgresEnum("auth", "oauth_response_type", new[] { "code" })
            .HasPostgresEnum("auth", "one_time_token_type", new[] { "confirmation_token", "reauthentication_token", "recovery_token", "email_change_token_new", "email_change_token_current", "phone_change_token" })
            .HasPostgresEnum("realtime", "action", new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE", "ERROR" })
            .HasPostgresEnum("realtime", "equality_op", new[] { "eq", "neq", "lt", "lte", "gt", "gte", "in" })
            .HasPostgresEnum("storage", "buckettype", new[] { "STANDARD", "ANALYTICS", "VECTOR" })
            .HasPostgresExtension("extensions", "pg_stat_statements")
            .HasPostgresExtension("extensions", "pgcrypto")
            .HasPostgresExtension("extensions", "uuid-ossp")
            .HasPostgresExtension("graphql", "pg_graphql")
            .HasPostgresExtension("vault", "supabase_vault");

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Account_pkey");

            entity.ToTable("Account", "seo");

            entity.HasIndex(e => new { e.Provider, e.ProviderAccountId }, "Account_provider_providerAccountId_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AccessToken).HasColumnName("access_token");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.IdToken).HasColumnName("id_token");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.ProviderAccountId).HasColumnName("providerAccountId");
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token");
            entity.Property(e => e.Scope).HasColumnName("scope");
            entity.Property(e => e.SessionState).HasColumnName("session_state");
            entity.Property(e => e.TokenType).HasColumnName("token_type");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.Accounts)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("Account_userId_fkey");
        });

        modelBuilder.Entity<AiUsageLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ai_usage_logs_pkey");

            entity.ToTable("ai_usage_logs");

            entity.HasIndex(e => new { e.RestaurantId, e.CalledAt }, "ai_usage_logs_restaurant_id_called_at_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.FeatureKey }, "ai_usage_logs_restaurant_id_feature_key_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CalledAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("called_at");
            entity.Property(e => e.EstimatedCostCents).HasColumnName("estimated_cost_cents");
            entity.Property(e => e.FeatureKey).HasColumnName("feature_key");
            entity.Property(e => e.InputTokens).HasColumnName("input_tokens");
            entity.Property(e => e.OutputTokens).HasColumnName("output_tokens");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.AiUsageLogs)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("ai_usage_logs_restaurant_id_fkey");
        });

        modelBuilder.Entity<Analytic>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Analytics_pkey");

            entity.ToTable("Analytics", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Clicks).HasColumnName("clicks");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Ctr).HasColumnName("ctr");
            entity.Property(e => e.Date)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("date");
            entity.Property(e => e.Impressions).HasColumnName("impressions");
            entity.Property(e => e.Keyword).HasColumnName("keyword");
            entity.Property(e => e.Page).HasColumnName("page");
            entity.Property(e => e.Position).HasColumnName("position");
            entity.Property(e => e.SiteId).HasColumnName("siteId");

            entity.HasOne(d => d.Site).WithMany(p => p.Analytics)
                .HasForeignKey(d => d.SiteId)
                .HasConstraintName("Analytics_siteId_fkey");
        });

        modelBuilder.Entity<Article>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Article_pkey");

            entity.ToTable("Article", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CmsPostId).HasColumnName("cmsPostId");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.FeaturedImageUrl).HasColumnName("featuredImageUrl");
            entity.Property(e => e.InternalLinks)
                .HasColumnType("jsonb")
                .HasColumnName("internalLinks");
            entity.Property(e => e.MetaDescription).HasColumnName("metaDescription");
            entity.Property(e => e.MetaTitle).HasColumnName("metaTitle");
            entity.Property(e => e.PublishedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("publishedAt");
            entity.Property(e => e.ReadabilityScore).HasColumnName("readabilityScore");
            entity.Property(e => e.SeoScore).HasColumnName("seoScore");
            entity.Property(e => e.SiteId).HasColumnName("siteId");
            entity.Property(e => e.Slug).HasColumnName("slug");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'draft'::text")
                .HasColumnName("status");
            entity.Property(e => e.TargetKeyword).HasColumnName("targetKeyword");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updatedAt");
            entity.Property(e => e.WordCount).HasColumnName("wordCount");

            entity.HasOne(d => d.Site).WithMany(p => p.Articles)
                .HasForeignKey(d => d.SiteId)
                .HasConstraintName("Article_siteId_fkey");
        });

        modelBuilder.Entity<Audience>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Audience_pkey");

            entity.ToTable("Audience", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Demographics).HasColumnName("demographics");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Goals).HasColumnName("goals");
            entity.Property(e => e.IsDefault).HasColumnName("isDefault");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PainPoints).HasColumnName("painPoints");
            entity.Property(e => e.SiteId).HasColumnName("siteId");

            entity.HasOne(d => d.Site).WithMany(p => p.Audiences)
                .HasForeignKey(d => d.SiteId)
                .HasConstraintName("Audience_siteId_fkey");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("audit_logs_pkey");

            entity.ToTable("audit_logs");

            entity.HasIndex(e => e.Action, "audit_logs_action_idx");

            entity.HasIndex(e => e.CreatedAt, "audit_logs_created_at_idx");

            entity.HasIndex(e => e.UserId, "audit_logs_user_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.UserAgent).HasColumnName("user_agent");
            entity.Property(e => e.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<AuditLog1>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("audit_logs_pkey");

            entity.ToTable("audit_logs", "audit");

            entity.HasIndex(e => e.Action, "audit_logs_action_idx");

            entity.HasIndex(e => e.AdminId, "audit_logs_adminId_idx");

            entity.HasIndex(e => e.TargetId, "audit_logs_targetId_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.AdminId).HasColumnName("adminId");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.TargetId).HasColumnName("targetId");
        });

        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("audit_log_entries_pkey");

            entity.ToTable("audit_log_entries", "auth", tb => tb.HasComment("Auth: Audit trail for user actions."));

            entity.HasIndex(e => e.InstanceId, "audit_logs_instance_id_idx");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.InstanceId).HasColumnName("instance_id");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(64)
                .HasDefaultValueSql("''::character varying")
                .HasColumnName("ip_address");
            entity.Property(e => e.Payload)
                .HasColumnType("json")
                .HasColumnName("payload");
        });

        modelBuilder.Entity<BrandVoice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("BrandVoice_pkey");

            entity.ToTable("BrandVoice", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Examples).HasColumnName("examples");
            entity.Property(e => e.IsDefault).HasColumnName("isDefault");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SiteId).HasColumnName("siteId");
            entity.Property(e => e.ToneWords).HasColumnName("toneWords");

            entity.HasOne(d => d.Site).WithMany(p => p.BrandVoices)
                .HasForeignKey(d => d.SiteId)
                .HasConstraintName("BrandVoice_siteId_fkey");
        });

        modelBuilder.Entity<BreakType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("break_types_pkey");

            entity.ToTable("break_types");

            entity.HasIndex(e => e.RestaurantId, "break_types_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpectedMinutes)
                .HasDefaultValue(15)
                .HasColumnName("expected_minutes");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsPaid).HasColumnName("is_paid");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.BreakTypes)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("break_types_restaurant_id_fkey");
        });

        modelBuilder.Entity<Bucket>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("buckets_pkey");

            entity.ToTable("buckets", "storage");

            entity.HasIndex(e => e.Name, "bname").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AllowedMimeTypes).HasColumnName("allowed_mime_types");
            entity.Property(e => e.AvifAutodetection)
                .HasDefaultValue(false)
                .HasColumnName("avif_autodetection");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.FileSizeLimit).HasColumnName("file_size_limit");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Owner)
                .HasComment("Field is deprecated, use owner_id instead")
                .HasColumnName("owner");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.Public)
                .HasDefaultValue(false)
                .HasColumnName("public");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<BucketsAnalytic>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("buckets_analytics_pkey");

            entity.ToTable("buckets_analytics", "storage");

            entity.HasIndex(e => e.Name, "buckets_analytics_unique_name_idx")
                .IsUnique()
                .HasFilter("(deleted_at IS NULL)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.Format)
                .HasDefaultValueSql("'ICEBERG'::text")
                .HasColumnName("format");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<BucketsVector>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("buckets_vectors_pkey");

            entity.ToTable("buckets_vectors", "storage");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Campaign_pkey");

            entity.ToTable("Campaign", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Brief).HasColumnName("brief");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.EndDate)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("endDate");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SiteId).HasColumnName("siteId");
            entity.Property(e => e.StartDate)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("startDate");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'active'::text")
                .HasColumnName("status");

            entity.HasOne(d => d.Site).WithMany(p => p.Campaigns)
                .HasForeignKey(d => d.SiteId)
                .HasConstraintName("Campaign_siteId_fkey");
        });

        modelBuilder.Entity<Campaign1>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("campaigns_pkey");

            entity.ToTable("campaigns");

            entity.HasIndex(e => e.RestaurantId, "campaigns_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AudienceLoyaltyTier).HasColumnName("audience_loyalty_tier");
            entity.Property(e => e.AudienceSegment).HasColumnName("audience_segment");
            entity.Property(e => e.Body).HasColumnName("body");
            entity.Property(e => e.Channel).HasColumnName("channel");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EstimatedRecipients).HasColumnName("estimated_recipients");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.ScheduledAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("scheduled_at");
            entity.Property(e => e.SentAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("sent_at");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'draft'::text")
                .HasColumnName("status");
            entity.Property(e => e.Subject).HasColumnName("subject");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Campaign1s)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("campaigns_restaurant_id_fkey");
        });

        modelBuilder.Entity<CampaignAsset>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("CampaignAsset_pkey");

            entity.ToTable("CampaignAsset", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CampaignId).HasColumnName("campaignId");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Platform).HasColumnName("platform");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'draft'::text")
                .HasColumnName("status");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Type).HasColumnName("type");

            entity.HasOne(d => d.Campaign).WithMany(p => p.CampaignAssets)
                .HasForeignKey(d => d.CampaignId)
                .HasConstraintName("CampaignAsset_campaignId_fkey");
        });

        modelBuilder.Entity<CampaignPerformance>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("campaign_performances_pkey");

            entity.ToTable("campaign_performances");

            entity.HasIndex(e => e.CampaignId, "campaign_performances_campaign_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CampaignId).HasColumnName("campaign_id");
            entity.Property(e => e.Clicked).HasColumnName("clicked");
            entity.Property(e => e.Converted).HasColumnName("converted");
            entity.Property(e => e.Delivered).HasColumnName("delivered");
            entity.Property(e => e.Opened).HasColumnName("opened");
            entity.Property(e => e.Revenue)
                .HasPrecision(10, 2)
                .HasColumnName("revenue");
            entity.Property(e => e.Sent).HasColumnName("sent");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Campaign).WithOne(p => p.CampaignPerformance)
                .HasForeignKey<CampaignPerformance>(d => d.CampaignId)
                .HasConstraintName("campaign_performances_campaign_id_fkey");
        });

        modelBuilder.Entity<CateringActivity>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("catering_activities_pkey");

            entity.ToTable("catering_activities");

            entity.HasIndex(e => e.JobId, "catering_activities_job_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.ActorType)
                .HasDefaultValueSql("'operator'::text")
                .HasColumnName("actor_type");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.JobId).HasColumnName("job_id");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");

            entity.HasOne(d => d.Job).WithMany(p => p.CateringActivities)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("catering_activities_job_id_fkey");
        });

        modelBuilder.Entity<CateringCapacitySetting>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("catering_capacity_settings_pkey");

            entity.ToTable("catering_capacity_settings");

            entity.HasIndex(e => e.RestaurantId, "catering_capacity_settings_restaurant_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ConflictAlertsEnabled)
                .HasDefaultValue(true)
                .HasColumnName("conflict_alerts_enabled");
            entity.Property(e => e.MaxEventsPerDay)
                .HasDefaultValue(3)
                .HasColumnName("max_events_per_day");
            entity.Property(e => e.MaxHeadcountPerDay)
                .HasDefaultValue(200)
                .HasColumnName("max_headcount_per_day");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithOne(p => p.CateringCapacitySetting)
                .HasForeignKey<CateringCapacitySetting>(d => d.RestaurantId)
                .HasConstraintName("catering_capacity_settings_restaurant_id_fkey");
        });

        modelBuilder.Entity<CateringEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("catering_events_pkey");

            entity.ToTable("catering_events");

            entity.HasIndex(e => new { e.RestaurantId, e.FulfillmentDate }, "catering_events_restaurant_id_fulfillment_date_idx");

            entity.HasIndex(e => e.RestaurantId, "catering_events_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Status }, "catering_events_restaurant_id_status_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AiContent)
                .HasColumnType("jsonb")
                .HasColumnName("ai_content");
            entity.Property(e => e.BookingDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("booking_date");
            entity.Property(e => e.BrandingColor).HasColumnName("branding_color");
            entity.Property(e => e.BrandingLogoUrl).HasColumnName("branding_logo_url");
            entity.Property(e => e.ClientEmail).HasColumnName("client_email");
            entity.Property(e => e.ClientName).HasColumnName("client_name");
            entity.Property(e => e.ClientPhone).HasColumnName("client_phone");
            entity.Property(e => e.CompanyName).HasColumnName("company_name");
            entity.Property(e => e.ContractSignedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("contract_signed_at");
            entity.Property(e => e.ContractUrl).HasColumnName("contract_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeliveryDetails)
                .HasColumnType("jsonb")
                .HasColumnName("delivery_details");
            entity.Property(e => e.DietaryRequirements)
                .HasColumnType("jsonb")
                .HasColumnName("dietary_requirements");
            entity.Property(e => e.EndTime)
                .HasMaxLength(5)
                .HasColumnName("end_time");
            entity.Property(e => e.EstimateId).HasColumnName("estimate_id");
            entity.Property(e => e.EventType).HasColumnName("event_type");
            entity.Property(e => e.FulfillmentDate).HasColumnName("fulfillment_date");
            entity.Property(e => e.GratuityCents).HasColumnName("gratuity_cents");
            entity.Property(e => e.GratuityPercent)
                .HasPrecision(5, 2)
                .HasColumnName("gratuity_percent");
            entity.Property(e => e.Headcount).HasColumnName("headcount");
            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.InvoiceNotes).HasColumnName("invoice_notes");
            entity.Property(e => e.LocationAddress).HasColumnName("location_address");
            entity.Property(e => e.LocationType)
                .HasDefaultValueSql("'on_site'::text")
                .HasColumnName("location_type");
            entity.Property(e => e.Milestones)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("milestones");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.Packages)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("packages");
            entity.Property(e => e.PaidCents).HasColumnName("paid_cents");
            entity.Property(e => e.ProposalSentAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("proposal_sent_at");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.SelectedPackageId).HasColumnName("selected_package_id");
            entity.Property(e => e.ServiceChargeCents).HasColumnName("service_charge_cents");
            entity.Property(e => e.ServiceChargePercent)
                .HasPrecision(5, 2)
                .HasColumnName("service_charge_percent");
            entity.Property(e => e.SignatureImageUrl).HasColumnName("signature_image_url");
            entity.Property(e => e.SignerConsentedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("signer_consented_at");
            entity.Property(e => e.SignerIp).HasColumnName("signer_ip");
            entity.Property(e => e.StartTime)
                .HasMaxLength(5)
                .HasColumnName("start_time");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'inquiry'::text")
                .HasColumnName("status");
            entity.Property(e => e.SubtotalCents).HasColumnName("subtotal_cents");
            entity.Property(e => e.Tastings)
                .HasColumnType("jsonb")
                .HasColumnName("tastings");
            entity.Property(e => e.TaxCents).HasColumnName("tax_cents");
            entity.Property(e => e.TaxPercent)
                .HasPrecision(5, 2)
                .HasColumnName("tax_percent");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.TotalCents).HasColumnName("total_cents");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.CateringEvents)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("catering_events_restaurant_id_fkey");
        });

        modelBuilder.Entity<CateringPackageTemplate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("catering_package_templates_pkey");

            entity.ToTable("catering_package_templates");

            entity.HasIndex(e => e.MerchantId, "catering_package_templates_merchant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.MenuItemIds)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("menu_item_ids");
            entity.Property(e => e.MerchantId).HasColumnName("merchant_id");
            entity.Property(e => e.MinimumHeadcount)
                .HasDefaultValue(1)
                .HasColumnName("minimum_headcount");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PricePerUnitCents).HasColumnName("price_per_unit_cents");
            entity.Property(e => e.PricingModel).HasColumnName("pricing_model");
            entity.Property(e => e.Tier).HasColumnName("tier");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<CateringProposalToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("catering_proposal_tokens_pkey");

            entity.ToTable("catering_proposal_tokens");

            entity.HasIndex(e => e.Token, "catering_proposal_tokens_token_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ApprovedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("approved_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.JobId).HasColumnName("job_id");
            entity.Property(e => e.Token).HasColumnName("token");
            entity.Property(e => e.ViewedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("viewed_at");

            entity.HasOne(d => d.Job).WithMany(p => p.CateringProposalTokens)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("catering_proposal_tokens_job_id_fkey");
        });

        modelBuilder.Entity<CheckDiscount>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("check_discounts_pkey");

            entity.ToTable("check_discounts");

            entity.HasIndex(e => e.CheckId, "check_discounts_check_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppliedBy).HasColumnName("applied_by");
            entity.Property(e => e.ApprovedBy).HasColumnName("approved_by");
            entity.Property(e => e.CheckId).HasColumnName("check_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.Value)
                .HasPrecision(10, 2)
                .HasColumnName("value");

            entity.HasOne(d => d.Check).WithMany(p => p.CheckDiscounts)
                .HasForeignKey(d => d.CheckId)
                .HasConstraintName("check_discounts_check_id_fkey");
        });

        modelBuilder.Entity<CheckItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("check_items_pkey");

            entity.ToTable("check_items");

            entity.HasIndex(e => e.CheckId, "check_items_check_id_idx");

            entity.HasIndex(e => e.OrderId, "check_items_order_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CheckId).HasColumnName("check_id");
            entity.Property(e => e.CompApprovedBy).HasColumnName("comp_approved_by");
            entity.Property(e => e.CompAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("comp_at");
            entity.Property(e => e.CompBy).HasColumnName("comp_by");
            entity.Property(e => e.CompReason).HasColumnName("comp_reason");
            entity.Property(e => e.CourseGuid).HasColumnName("course_guid");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.FulfillmentStatus)
                .HasDefaultValueSql("'NEW'::text")
                .HasColumnName("fulfillment_status");
            entity.Property(e => e.IsComped).HasColumnName("is_comped");
            entity.Property(e => e.MenuItemId).HasColumnName("menu_item_id");
            entity.Property(e => e.MenuItemName).HasColumnName("menu_item_name");
            entity.Property(e => e.ModifiersPrice)
                .HasPrecision(10, 2)
                .HasColumnName("modifiers_price");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.SeatNumber).HasColumnName("seat_number");
            entity.Property(e => e.SpecialInstructions).HasColumnName("special_instructions");
            entity.Property(e => e.TotalPrice)
                .HasPrecision(10, 2)
                .HasColumnName("total_price");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(10, 2)
                .HasColumnName("unit_price");

            entity.HasOne(d => d.Check).WithMany(p => p.CheckItems)
                .HasForeignKey(d => d.CheckId)
                .HasConstraintName("check_items_check_id_fkey");
        });

        modelBuilder.Entity<CheckItemModifier>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("check_item_modifiers_pkey");

            entity.ToTable("check_item_modifiers");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CheckItemId).HasColumnName("check_item_id");
            entity.Property(e => e.ModifierId).HasColumnName("modifier_id");
            entity.Property(e => e.ModifierName).HasColumnName("modifier_name");
            entity.Property(e => e.PriceAdjustment)
                .HasPrecision(10, 2)
                .HasColumnName("price_adjustment");

            entity.HasOne(d => d.CheckItem).WithMany(p => p.CheckItemModifiers)
                .HasForeignKey(d => d.CheckItemId)
                .HasConstraintName("check_item_modifiers_check_item_id_fkey");
        });

        modelBuilder.Entity<CheckVoidedItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("check_voided_items_pkey");

            entity.ToTable("check_voided_items");

            entity.HasIndex(e => e.CheckId, "check_voided_items_check_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CheckId).HasColumnName("check_id");
            entity.Property(e => e.CheckItemId).HasColumnName("check_item_id");
            entity.Property(e => e.ManagerApproval).HasColumnName("manager_approval");
            entity.Property(e => e.MenuItemName).HasColumnName("menu_item_name");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.TotalPrice)
                .HasPrecision(10, 2)
                .HasColumnName("total_price");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(10, 2)
                .HasColumnName("unit_price");
            entity.Property(e => e.VoidReason).HasColumnName("void_reason");
            entity.Property(e => e.VoidedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("voided_at");
            entity.Property(e => e.VoidedBy).HasColumnName("voided_by");

            entity.HasOne(d => d.Check).WithMany(p => p.CheckVoidedItems)
                .HasForeignKey(d => d.CheckId)
                .HasConstraintName("check_voided_items_check_id_fkey");
        });

        modelBuilder.Entity<CircuitReset>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("circuit_resets_pkey");

            entity.ToTable("circuit_resets", "audit");

            entity.HasIndex(e => e.ActionBySlackId, "circuit_resets_actionBySlackId_idx");

            entity.HasIndex(e => e.ServiceName, "circuit_resets_serviceName_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ActionBySlackId).HasColumnName("actionBySlackId");
            entity.Property(e => e.ActionByUsername).HasColumnName("actionByUsername");
            entity.Property(e => e.CurrentState).HasColumnName("currentState");
            entity.Property(e => e.ExecutedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("executedAt");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.PreviousState).HasColumnName("previousState");
            entity.Property(e => e.ServiceName).HasColumnName("serviceName");
        });

        modelBuilder.Entity<Combo>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("combos_pkey");

            entity.ToTable("combos");

            entity.HasIndex(e => e.RestaurantId, "combos_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ComboPrice)
                .HasPrecision(10, 2)
                .HasColumnName("combo_price");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Items)
                .HasColumnType("jsonb")
                .HasColumnName("items");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Combos)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("combos_restaurant_id_fkey");
        });

        modelBuilder.Entity<CommissionRule>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("commission_rules_pkey");

            entity.ToTable("commission_rules");

            entity.HasIndex(e => e.RestaurantId, "commission_rules_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppliesTo)
                .HasDefaultValueSql("'sales'::text")
                .HasColumnName("applies_to");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.JobTitles)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("job_titles");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Value)
                .HasPrecision(10, 2)
                .HasColumnName("value");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.CommissionRules)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("commission_rules_restaurant_id_fkey");
        });

        modelBuilder.Entity<ComplianceAlert>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("compliance_alerts_pkey");

            entity.ToTable("compliance_alerts");

            entity.HasIndex(e => e.RestaurantId, "compliance_alerts_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.IsResolved }, "compliance_alerts_restaurant_id_is_resolved_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.IsResolved).HasColumnName("is_resolved");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Severity)
                .HasDefaultValueSql("'warning'::text")
                .HasColumnName("severity");
            entity.Property(e => e.TeamMemberId).HasColumnName("team_member_id");
            entity.Property(e => e.Type).HasColumnName("type");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.ComplianceAlerts)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("compliance_alerts_restaurant_id_fkey");
        });

        modelBuilder.Entity<ContentCalendar>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ContentCalendar_pkey");

            entity.ToTable("ContentCalendar", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ArticleId).HasColumnName("articleId");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Keyword).HasColumnName("keyword");
            entity.Property(e => e.ScheduledAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("scheduledAt");
            entity.Property(e => e.SiteId).HasColumnName("siteId");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'::text")
                .HasColumnName("status");

            entity.HasOne(d => d.Site).WithMany(p => p.ContentCalendars)
                .HasForeignKey(d => d.SiteId)
                .HasConstraintName("ContentCalendar_siteId_fkey");
        });

        modelBuilder.Entity<ContentTemplate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ContentTemplate_pkey");

            entity.ToTable("ContentTemplate", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Fields)
                .HasColumnType("jsonb")
                .HasColumnName("fields");
            entity.Property(e => e.IsBuiltIn).HasColumnName("isBuiltIn");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Prompt).HasColumnName("prompt");
        });

        modelBuilder.Entity<CustomOauthProvider>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("custom_oauth_providers_pkey");

            entity.ToTable("custom_oauth_providers", "auth");

            entity.HasIndex(e => e.CreatedAt, "custom_oauth_providers_created_at_idx");

            entity.HasIndex(e => e.Enabled, "custom_oauth_providers_enabled_idx");

            entity.HasIndex(e => e.Identifier, "custom_oauth_providers_identifier_idx");

            entity.HasIndex(e => e.Identifier, "custom_oauth_providers_identifier_key").IsUnique();

            entity.HasIndex(e => e.ProviderType, "custom_oauth_providers_provider_type_idx");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.AcceptableClientIds)
                .HasDefaultValueSql("'{}'::text[]")
                .HasColumnName("acceptable_client_ids");
            entity.Property(e => e.AttributeMapping)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("attribute_mapping");
            entity.Property(e => e.AuthorizationParams)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("authorization_params");
            entity.Property(e => e.AuthorizationUrl).HasColumnName("authorization_url");
            entity.Property(e => e.CachedDiscovery)
                .HasColumnType("jsonb")
                .HasColumnName("cached_discovery");
            entity.Property(e => e.ClientId).HasColumnName("client_id");
            entity.Property(e => e.ClientSecret).HasColumnName("client_secret");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DiscoveryCachedAt).HasColumnName("discovery_cached_at");
            entity.Property(e => e.DiscoveryUrl).HasColumnName("discovery_url");
            entity.Property(e => e.EmailOptional).HasColumnName("email_optional");
            entity.Property(e => e.Enabled)
                .HasDefaultValue(true)
                .HasColumnName("enabled");
            entity.Property(e => e.Identifier).HasColumnName("identifier");
            entity.Property(e => e.Issuer).HasColumnName("issuer");
            entity.Property(e => e.JwksUri).HasColumnName("jwks_uri");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PkceEnabled)
                .HasDefaultValue(true)
                .HasColumnName("pkce_enabled");
            entity.Property(e => e.ProviderType).HasColumnName("provider_type");
            entity.Property(e => e.Scopes)
                .HasDefaultValueSql("'{}'::text[]")
                .HasColumnName("scopes");
            entity.Property(e => e.SkipNonceCheck).HasColumnName("skip_nonce_check");
            entity.Property(e => e.TokenUrl).HasColumnName("token_url");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserinfoUrl).HasColumnName("userinfo_url");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("customers_pkey");

            entity.ToTable("customers");

            entity.HasIndex(e => new { e.RestaurantId, e.Phone }, "customers_restaurant_id_phone_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AverageOrderValue)
                .HasPrecision(10, 2)
                .HasColumnName("average_order_value");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.FirstName).HasColumnName("first_name");
            entity.Property(e => e.LastName).HasColumnName("last_name");
            entity.Property(e => e.LastOrderDate)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_order_date");
            entity.Property(e => e.LoyaltyPoints).HasColumnName("loyalty_points");
            entity.Property(e => e.LoyaltyTier)
                .HasDefaultValueSql("'bronze'::text")
                .HasColumnName("loyalty_tier");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Tags)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("tags");
            entity.Property(e => e.TotalOrders).HasColumnName("total_orders");
            entity.Property(e => e.TotalPointsEarned).HasColumnName("total_points_earned");
            entity.Property(e => e.TotalPointsRedeemed).HasColumnName("total_points_redeemed");
            entity.Property(e => e.TotalSpent)
                .HasPrecision(10, 2)
                .HasColumnName("total_spent");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Customers)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("customers_restaurant_id_fkey");
        });

        modelBuilder.Entity<CustomerFeedback>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("customer_feedback_pkey");

            entity.ToTable("customer_feedback");

            entity.HasIndex(e => e.RestaurantId, "customer_feedback_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Rating }, "customer_feedback_restaurant_id_rating_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Rating).HasColumnName("rating");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Source)
                .HasDefaultValueSql("'in_app'::text")
                .HasColumnName("source");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.CustomerFeedbacks)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("customer_feedback_restaurant_id_fkey");
        });

        modelBuilder.Entity<CycleCount>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("cycle_counts_pkey");

            entity.ToTable("cycle_counts");

            entity.HasIndex(e => e.RestaurantId, "cycle_counts_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'in_progress'::text")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.CycleCounts)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("cycle_counts_restaurant_id_fkey");
        });

        modelBuilder.Entity<CycleCountItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("cycle_count_items_pkey");

            entity.ToTable("cycle_count_items");

            entity.HasIndex(e => e.CycleCountId, "cycle_count_items_cycle_count_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ActualQty)
                .HasPrecision(10, 2)
                .HasColumnName("actual_qty");
            entity.Property(e => e.CycleCountId).HasColumnName("cycle_count_id");
            entity.Property(e => e.ExpectedQty)
                .HasPrecision(10, 2)
                .HasColumnName("expected_qty");
            entity.Property(e => e.InventoryItemId).HasColumnName("inventory_item_id");
            entity.Property(e => e.Variance)
                .HasPrecision(10, 2)
                .HasColumnName("variance");

            entity.HasOne(d => d.CycleCount).WithMany(p => p.CycleCountItems)
                .HasForeignKey(d => d.CycleCountId)
                .HasConstraintName("cycle_count_items_cycle_count_id_fkey");
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Device_pkey");

            entity.ToTable("Device", "geek_auth");

            entity.HasIndex(e => new { e.UserId, e.DeviceId }, "Device_userId_deviceId_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.DeviceId).HasColumnName("deviceId");
            entity.Property(e => e.IpAddress).HasColumnName("ipAddress");
            entity.Property(e => e.LastLoginAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("lastLoginAt");
            entity.Property(e => e.MfaExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("mfaExpiresAt");
            entity.Property(e => e.MfaVerifiedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("mfaVerifiedAt");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'active'::text")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updatedAt");
            entity.Property(e => e.UserAgent).HasColumnName("userAgent");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.Devices)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("Device_userId_fkey");
        });

        modelBuilder.Entity<Device1>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("devices_pkey");

            entity.ToTable("devices");

            entity.HasIndex(e => e.DeviceCode, "devices_device_code_idx");

            entity.HasIndex(e => e.DeviceCode, "devices_device_code_key").IsUnique();

            entity.HasIndex(e => e.RestaurantId, "devices_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeviceCode).HasColumnName("device_code");
            entity.Property(e => e.DeviceName).HasColumnName("device_name");
            entity.Property(e => e.DeviceType).HasColumnName("device_type");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.HardwareInfo)
                .HasColumnType("jsonb")
                .HasColumnName("hardware_info");
            entity.Property(e => e.LastSeenAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_seen_at");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.MfaExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("mfa_expires_at");
            entity.Property(e => e.MfaVerifiedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("mfa_verified_at");
            entity.Property(e => e.ModeId).HasColumnName("mode_id");
            entity.Property(e => e.PairedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("paired_at");
            entity.Property(e => e.PosMode).HasColumnName("pos_mode");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'::text")
                .HasColumnName("status");
            entity.Property(e => e.TeamMemberId).HasColumnName("team_member_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Mode).WithMany(p => p.Device1s)
                .HasForeignKey(d => d.ModeId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("devices_mode_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Device1s)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("devices_restaurant_id_fkey");

            entity.HasOne(d => d.TeamMember).WithMany(p => p.Device1s)
                .HasForeignKey(d => d.TeamMemberId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("devices_team_member_id_fkey");
        });

        modelBuilder.Entity<DeviceMode>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("device_modes_pkey");

            entity.ToTable("device_modes");

            entity.HasIndex(e => e.RestaurantId, "device_modes_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Name }, "device_modes_restaurant_id_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeviceType).HasColumnName("device_type");
            entity.Property(e => e.IsDefault).HasColumnName("is_default");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Settings)
                .HasColumnType("jsonb")
                .HasColumnName("settings");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.DeviceModes)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("device_modes_restaurant_id_fkey");
        });

        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("events_pkey");

            entity.ToTable("events");

            entity.HasIndex(e => e.RestaurantId, "events_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CurrentRsvps).HasColumnName("current_rsvps");
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EndTime)
                .HasMaxLength(5)
                .HasColumnName("end_time");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.MaxCapacity).HasColumnName("max_capacity");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.StartTime)
                .HasMaxLength(5)
                .HasColumnName("start_time");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Events)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("events_restaurant_id_fkey");
        });

        modelBuilder.Entity<FlowState>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("flow_state_pkey");

            entity.ToTable("flow_state", "auth", tb => tb.HasComment("Stores metadata for all OAuth/SSO login flows"));

            entity.HasIndex(e => e.CreatedAt, "flow_state_created_at_idx").IsDescending();

            entity.HasIndex(e => e.AuthCode, "idx_auth_code");

            entity.HasIndex(e => new { e.UserId, e.AuthenticationMethod }, "idx_user_id_auth_method");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AuthCode).HasColumnName("auth_code");
            entity.Property(e => e.AuthCodeIssuedAt).HasColumnName("auth_code_issued_at");
            entity.Property(e => e.AuthenticationMethod).HasColumnName("authentication_method");
            entity.Property(e => e.CodeChallenge).HasColumnName("code_challenge");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.EmailOptional).HasColumnName("email_optional");
            entity.Property(e => e.InviteToken).HasColumnName("invite_token");
            entity.Property(e => e.LinkingTargetId).HasColumnName("linking_target_id");
            entity.Property(e => e.OauthClientStateId).HasColumnName("oauth_client_state_id");
            entity.Property(e => e.ProviderAccessToken).HasColumnName("provider_access_token");
            entity.Property(e => e.ProviderRefreshToken).HasColumnName("provider_refresh_token");
            entity.Property(e => e.ProviderType).HasColumnName("provider_type");
            entity.Property(e => e.Referrer).HasColumnName("referrer");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
        });

        modelBuilder.Entity<FoodCostRecipe>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("food_cost_recipes_pkey");

            entity.ToTable("food_cost_recipes");

            entity.HasIndex(e => e.MenuItemId, "food_cost_recipes_menu_item_id_idx");

            entity.HasIndex(e => e.RestaurantId, "food_cost_recipes_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.MenuItemId).HasColumnName("menu_item_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.YieldQty)
                .HasPrecision(10, 2)
                .HasColumnName("yield_qty");
            entity.Property(e => e.YieldUnit).HasColumnName("yield_unit");

            entity.HasOne(d => d.MenuItem).WithMany(p => p.FoodCostRecipes)
                .HasForeignKey(d => d.MenuItemId)
                .HasConstraintName("food_cost_recipes_menu_item_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.FoodCostRecipes)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("food_cost_recipes_restaurant_id_fkey");
        });

        modelBuilder.Entity<FoodCostRecipeIngredient>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("food_cost_recipe_ingredients_pkey");

            entity.ToTable("food_cost_recipe_ingredients");

            entity.HasIndex(e => e.RecipeId, "food_cost_recipe_ingredients_recipe_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EstimatedUnitCost)
                .HasPrecision(10, 4)
                .HasColumnName("estimated_unit_cost");
            entity.Property(e => e.IngredientName).HasColumnName("ingredient_name");
            entity.Property(e => e.Quantity)
                .HasPrecision(10, 4)
                .HasColumnName("quantity");
            entity.Property(e => e.RecipeId).HasColumnName("recipe_id");
            entity.Property(e => e.Unit).HasColumnName("unit");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Recipe).WithMany(p => p.FoodCostRecipeIngredients)
                .HasForeignKey(d => d.RecipeId)
                .HasConstraintName("food_cost_recipe_ingredients_recipe_id_fkey");
        });

        modelBuilder.Entity<GiftCard>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("gift_cards_pkey");

            entity.ToTable("gift_cards");

            entity.HasIndex(e => e.Code, "gift_cards_code_key").IsUnique();

            entity.HasIndex(e => e.RestaurantId, "gift_cards_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CurrentBalance)
                .HasPrecision(10, 2)
                .HasColumnName("current_balance");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.InitialBalance)
                .HasPrecision(10, 2)
                .HasColumnName("initial_balance");
            entity.Property(e => e.PurchasedBy).HasColumnName("purchased_by");
            entity.Property(e => e.RecipientEmail).HasColumnName("recipient_email");
            entity.Property(e => e.RecipientName).HasColumnName("recipient_name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'active'::text")
                .HasColumnName("status");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.GiftCards)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("gift_cards_restaurant_id_fkey");
        });

        modelBuilder.Entity<GiftCardRedemption>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("gift_card_redemptions_pkey");

            entity.ToTable("gift_card_redemptions");

            entity.HasIndex(e => e.GiftCardId, "gift_card_redemptions_gift_card_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(10, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.GiftCardId).HasColumnName("gift_card_id");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.RedeemedBy).HasColumnName("redeemed_by");

            entity.HasOne(d => d.GiftCard).WithMany(p => p.GiftCardRedemptions)
                .HasForeignKey(d => d.GiftCardId)
                .HasConstraintName("gift_card_redemptions_gift_card_id_fkey");
        });

        modelBuilder.Entity<HouseAccount>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("house_accounts_pkey");

            entity.ToTable("house_accounts");

            entity.HasIndex(e => e.RestaurantId, "house_accounts_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ContactEmail).HasColumnName("contact_email");
            entity.Property(e => e.ContactName).HasColumnName("contact_name");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreditLimit)
                .HasPrecision(10, 2)
                .HasColumnName("credit_limit");
            entity.Property(e => e.CurrentBalance)
                .HasPrecision(10, 2)
                .HasColumnName("current_balance");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'active'::text")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.HouseAccounts)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("house_accounts_restaurant_id_fkey");
        });

        modelBuilder.Entity<Identity>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("identities_pkey");

            entity.ToTable("identities", "auth", tb => tb.HasComment("Auth: Stores identities associated to a user."));

            entity.HasIndex(e => e.Email, "identities_email_idx").HasOperators(new[] { "text_pattern_ops" });

            entity.HasIndex(e => new { e.ProviderId, e.Provider }, "identities_provider_id_provider_unique").IsUnique();

            entity.HasIndex(e => e.UserId, "identities_user_id_idx");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasComputedColumnSql("lower((identity_data ->> 'email'::text))", true)
                .HasComment("Auth: Email is a generated column that references the optional email property in the identity_data")
                .HasColumnName("email");
            entity.Property(e => e.IdentityData)
                .HasColumnType("jsonb")
                .HasColumnName("identity_data");
            entity.Property(e => e.LastSignInAt).HasColumnName("last_sign_in_at");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.Identities)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("identities_user_id_fkey");
        });

        modelBuilder.Entity<Instance>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("instances_pkey");

            entity.ToTable("instances", "auth", tb => tb.HasComment("Auth: Manages users across multiple sites."));

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.RawBaseConfig).HasColumnName("raw_base_config");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.Uuid).HasColumnName("uuid");
        });

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("inventory_items_pkey");

            entity.ToTable("inventory_items");

            entity.HasIndex(e => new { e.RestaurantId, e.Category }, "inventory_items_restaurant_id_category_idx");

            entity.HasIndex(e => e.RestaurantId, "inventory_items_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.Category)
                .HasMaxLength(100)
                .HasDefaultValueSql("'general'::character varying")
                .HasColumnName("category");
            entity.Property(e => e.CostPerUnit)
                .HasPrecision(10, 4)
                .HasColumnName("cost_per_unit");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CurrentStock)
                .HasPrecision(10, 2)
                .HasColumnName("current_stock");
            entity.Property(e => e.ExpirationDate)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expiration_date");
            entity.Property(e => e.LastCountDate)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_count_date");
            entity.Property(e => e.LastRestocked)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_restocked");
            entity.Property(e => e.MaxStock)
                .HasPrecision(10, 2)
                .HasDefaultValue(100m)
                .HasColumnName("max_stock");
            entity.Property(e => e.MinStock)
                .HasPrecision(10, 2)
                .HasColumnName("min_stock");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.NameEn)
                .HasMaxLength(255)
                .HasColumnName("name_en");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Supplier)
                .HasMaxLength(255)
                .HasColumnName("supplier");
            entity.Property(e => e.Unit)
                .HasMaxLength(50)
                .HasDefaultValueSql("'units'::character varying")
                .HasColumnName("unit");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.InventoryItems)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("inventory_items_restaurant_id_fkey");
        });

        modelBuilder.Entity<InventoryLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("inventory_logs_pkey");

            entity.ToTable("inventory_logs");

            entity.HasIndex(e => e.CreatedAt, "inventory_logs_created_at_idx");

            entity.HasIndex(e => e.InventoryItemId, "inventory_logs_inventory_item_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChangeAmount)
                .HasPrecision(10, 2)
                .HasColumnName("change_amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(255)
                .HasColumnName("created_by");
            entity.Property(e => e.InventoryItemId).HasColumnName("inventory_item_id");
            entity.Property(e => e.NewStock)
                .HasPrecision(10, 2)
                .HasColumnName("new_stock");
            entity.Property(e => e.PreviousStock)
                .HasPrecision(10, 2)
                .HasColumnName("previous_stock");
            entity.Property(e => e.Reason)
                .HasMaxLength(255)
                .HasColumnName("reason");

            entity.HasOne(d => d.InventoryItem).WithMany(p => p.InventoryLogs)
                .HasForeignKey(d => d.InventoryItemId)
                .HasConstraintName("inventory_logs_inventory_item_id_fkey");
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("invoices_pkey");

            entity.ToTable("invoices");

            entity.HasIndex(e => e.HouseAccountId, "invoices_house_account_id_idx");

            entity.HasIndex(e => e.InvoiceNumber, "invoices_invoice_number_key").IsUnique();

            entity.HasIndex(e => e.RestaurantId, "invoices_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerEmail).HasColumnName("customer_email");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.CustomerName).HasColumnName("customer_name");
            entity.Property(e => e.DueDate)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("due_date");
            entity.Property(e => e.HouseAccountId).HasColumnName("house_account_id");
            entity.Property(e => e.InvoiceNumber).HasColumnName("invoice_number");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.PaidAmount)
                .HasPrecision(10, 2)
                .HasColumnName("paid_amount");
            entity.Property(e => e.PaidAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("paid_at");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.SentAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("sent_at");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'draft'::text")
                .HasColumnName("status");
            entity.Property(e => e.Subtotal)
                .HasPrecision(10, 2)
                .HasColumnName("subtotal");
            entity.Property(e => e.Tax)
                .HasPrecision(10, 2)
                .HasColumnName("tax");
            entity.Property(e => e.Total)
                .HasPrecision(10, 2)
                .HasColumnName("total");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.HouseAccount).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.HouseAccountId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("invoices_house_account_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Invoices)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("invoices_restaurant_id_fkey");
        });

        modelBuilder.Entity<InvoiceLineItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("invoice_line_items_pkey");

            entity.ToTable("invoice_line_items");

            entity.HasIndex(e => e.InvoiceId, "invoice_line_items_invoice_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.Total)
                .HasPrecision(10, 2)
                .HasColumnName("total");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(10, 2)
                .HasColumnName("unit_price");

            entity.HasOne(d => d.Invoice).WithMany(p => p.InvoiceLineItems)
                .HasForeignKey(d => d.InvoiceId)
                .HasConstraintName("invoice_line_items_invoice_id_fkey");
        });

        modelBuilder.Entity<Keyword>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Keyword_pkey");

            entity.ToTable("Keyword", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Difficulty).HasColumnName("difficulty");
            entity.Property(e => e.Intent).HasColumnName("intent");
            entity.Property(e => e.Keyword1).HasColumnName("keyword");
            entity.Property(e => e.LongTail).HasColumnName("longTail");
            entity.Property(e => e.SearchVolume).HasColumnName("searchVolume");
            entity.Property(e => e.SiteId).HasColumnName("siteId");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'discovered'::text")
                .HasColumnName("status");
            entity.Property(e => e.SuggestedTitle).HasColumnName("suggestedTitle");
            entity.Property(e => e.TopicCluster).HasColumnName("topicCluster");

            entity.HasOne(d => d.Site).WithMany(p => p.Keywords)
                .HasForeignKey(d => d.SiteId)
                .HasConstraintName("Keyword_siteId_fkey");
        });

        modelBuilder.Entity<KioskProfile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("kiosk_profiles_pkey");

            entity.ToTable("kiosk_profiles");

            entity.HasIndex(e => e.RestaurantId, "kiosk_profiles_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Name }, "kiosk_profiles_restaurant_id_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EnableAccessibility).HasColumnName("enable_accessibility");
            entity.Property(e => e.EnabledCategories)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("enabled_categories");
            entity.Property(e => e.MaxIdleSeconds)
                .HasDefaultValue(120)
                .HasColumnName("max_idle_seconds");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PosMode).HasColumnName("pos_mode");
            entity.Property(e => e.RequireNameForOrder).HasColumnName("require_name_for_order");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.ShowImages)
                .HasDefaultValue(true)
                .HasColumnName("show_images");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.WelcomeMessage)
                .HasDefaultValueSql("'Welcome!'::text")
                .HasColumnName("welcome_message");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.KioskProfiles)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("kiosk_profiles_restaurant_id_fkey");
        });

        modelBuilder.Entity<KnowledgeBase>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("KnowledgeBase_pkey");

            entity.ToTable("KnowledgeBase", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SiteId).HasColumnName("siteId");
            entity.Property(e => e.SourceUrl).HasColumnName("sourceUrl");
            entity.Property(e => e.Type).HasColumnName("type");

            entity.HasOne(d => d.Site).WithMany(p => p.KnowledgeBases)
                .HasForeignKey(d => d.SiteId)
                .HasConstraintName("KnowledgeBase_siteId_fkey");
        });

        modelBuilder.Entity<LaborTarget>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("labor_targets_pkey");

            entity.ToTable("labor_targets");

            entity.HasIndex(e => new { e.RestaurantId, e.DayOfWeek }, "labor_targets_restaurant_id_dayOfWeek_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DayOfWeek).HasColumnName("dayOfWeek");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.TargetCost)
                .HasPrecision(10, 2)
                .HasColumnName("target_cost");
            entity.Property(e => e.TargetPercent)
                .HasPrecision(5, 2)
                .HasColumnName("target_percent");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<Layaway>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("layaways_pkey");

            entity.ToTable("layaways");

            entity.HasIndex(e => e.RestaurantId, "layaways_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.CustomerName).HasColumnName("customer_name");
            entity.Property(e => e.DepositPaid)
                .HasPrecision(10, 2)
                .HasColumnName("deposit_paid");
            entity.Property(e => e.Items)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("items");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'active'::text")
                .HasColumnName("status");
            entity.Property(e => e.TotalAmount)
                .HasPrecision(10, 2)
                .HasColumnName("total_amount");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Layaways)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("layaways_restaurant_id_fkey");
        });

        modelBuilder.Entity<LocationGroup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("location_groups_pkey");

            entity.ToTable("location_groups");

            entity.HasIndex(e => e.RestaurantGroupId, "location_groups_restaurant_group_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantGroupId).HasColumnName("restaurant_group_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.RestaurantGroup).WithMany(p => p.LocationGroups)
                .HasForeignKey(d => d.RestaurantGroupId)
                .HasConstraintName("location_groups_restaurant_group_id_fkey");
        });

        modelBuilder.Entity<LocationGroupMember>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("location_group_members_pkey");

            entity.ToTable("location_group_members");

            entity.HasIndex(e => new { e.LocationGroupId, e.RestaurantId }, "location_group_members_location_group_id_restaurant_id_key").IsUnique();

            entity.HasIndex(e => e.RestaurantId, "location_group_members_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.LocationGroupId).HasColumnName("location_group_id");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");

            entity.HasOne(d => d.LocationGroup).WithMany(p => p.LocationGroupMembers)
                .HasForeignKey(d => d.LocationGroupId)
                .HasConstraintName("location_group_members_location_group_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.LocationGroupMembers)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("location_group_members_restaurant_id_fkey");
        });

        modelBuilder.Entity<LoyaltyReward>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("loyalty_rewards_pkey");

            entity.ToTable("loyalty_rewards");

            entity.HasIndex(e => e.RestaurantId, "loyalty_rewards_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DiscountType).HasColumnName("discount_type");
            entity.Property(e => e.DiscountValue)
                .HasPrecision(10, 2)
                .HasColumnName("discount_value");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.MinTier)
                .HasDefaultValueSql("'bronze'::text")
                .HasColumnName("min_tier");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PointsCost).HasColumnName("points_cost");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.LoyaltyRewards)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("loyalty_rewards_restaurant_id_fkey");
        });

        modelBuilder.Entity<LoyaltyTransaction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("loyalty_transactions_pkey");

            entity.ToTable("loyalty_transactions");

            entity.HasIndex(e => e.CustomerId, "loyalty_transactions_customer_id_idx");

            entity.HasIndex(e => e.RestaurantId, "loyalty_transactions_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BalanceAfter).HasColumnName("balance_after");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Points).HasColumnName("points");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Type).HasColumnName("type");

            entity.HasOne(d => d.Customer).WithMany(p => p.LoyaltyTransactions)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("loyalty_transactions_customer_id_fkey");

            entity.HasOne(d => d.Order).WithMany(p => p.LoyaltyTransactions)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("loyalty_transactions_order_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.LoyaltyTransactions)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("loyalty_transactions_restaurant_id_fkey");
        });

        modelBuilder.Entity<MarketingAutomation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("marketing_automations_pkey");

            entity.ToTable("marketing_automations");

            entity.HasIndex(e => e.RestaurantId, "marketing_automations_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Action)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("action");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LastRunAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_run_at");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Trigger).HasColumnName("trigger");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.MarketingAutomations)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("marketing_automations_restaurant_id_fkey");
        });

        modelBuilder.Entity<MarketplaceIntegration>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("marketplace_integrations_pkey");

            entity.ToTable("marketplace_integrations");

            entity.HasIndex(e => new { e.Provider, e.ExternalStoreId }, "marketplace_integrations_provider_external_store_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Provider }, "marketplace_integrations_restaurant_id_provider_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.ExternalStoreId).HasColumnName("external_store_id");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.WebhookSigningSecretEncrypted).HasColumnName("webhook_signing_secret_encrypted");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.MarketplaceIntegrations)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("marketplace_integrations_restaurant_id_fkey");
        });

        modelBuilder.Entity<MarketplaceMenuMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("marketplace_menu_mappings_pkey");

            entity.ToTable("marketplace_menu_mappings");

            entity.HasIndex(e => e.MenuItemId, "marketplace_menu_mappings_menu_item_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Provider, e.ExternalItemId }, "marketplace_menu_mappings_restaurant_id_provider_external_i_key").IsUnique();

            entity.HasIndex(e => new { e.RestaurantId, e.Provider }, "marketplace_menu_mappings_restaurant_id_provider_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.ExternalItemId).HasColumnName("external_item_id");
            entity.Property(e => e.ExternalItemName).HasColumnName("external_item_name");
            entity.Property(e => e.MenuItemId).HasColumnName("menu_item_id");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.MenuItem).WithMany(p => p.MarketplaceMenuMappings)
                .HasForeignKey(d => d.MenuItemId)
                .HasConstraintName("marketplace_menu_mappings_menu_item_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.MarketplaceMenuMappings)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("marketplace_menu_mappings_restaurant_id_fkey");
        });

        modelBuilder.Entity<MarketplaceOrder>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("marketplace_orders_pkey");

            entity.ToTable("marketplace_orders");

            entity.HasIndex(e => e.OrderId, "marketplace_orders_order_id_key").IsUnique();

            entity.HasIndex(e => new { e.Provider, e.ExternalOrderId }, "marketplace_orders_provider_external_order_id_key").IsUnique();

            entity.HasIndex(e => new { e.RestaurantId, e.Provider, e.Status }, "marketplace_orders_restaurant_id_provider_status_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.ExternalCustomerId).HasColumnName("external_customer_id");
            entity.Property(e => e.ExternalOrderId).HasColumnName("external_order_id");
            entity.Property(e => e.ExternalStoreId).HasColumnName("external_store_id");
            entity.Property(e => e.IntegrationId).HasColumnName("integration_id");
            entity.Property(e => e.LastEventId).HasColumnName("last_event_id");
            entity.Property(e => e.LastPushAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_push_at");
            entity.Property(e => e.LastPushError).HasColumnName("last_push_error");
            entity.Property(e => e.LastPushResult).HasColumnName("last_push_result");
            entity.Property(e => e.LastPushedStatus).HasColumnName("last_pushed_status");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.RawPayload)
                .HasColumnType("jsonb")
                .HasColumnName("raw_payload");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'RECEIVED'::text")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Integration).WithMany(p => p.MarketplaceOrders)
                .HasForeignKey(d => d.IntegrationId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("marketplace_orders_integration_id_fkey");

            entity.HasOne(d => d.Order).WithOne(p => p.MarketplaceOrder)
                .HasForeignKey<MarketplaceOrder>(d => d.OrderId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("marketplace_orders_order_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.MarketplaceOrders)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("marketplace_orders_restaurant_id_fkey");
        });

        modelBuilder.Entity<MarketplaceStatusSyncJob>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("marketplace_status_sync_jobs_pkey");

            entity.ToTable("marketplace_status_sync_jobs");

            entity.HasIndex(e => new { e.MarketplaceOrderId, e.TargetStatus }, "marketplace_status_sync_jobs_marketplace_order_id_target_st_key").IsUnique();

            entity.HasIndex(e => new { e.RestaurantId, e.Status, e.NextAttemptAt }, "marketplace_status_sync_jobs_restaurant_id_status_next_atte_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.ExternalOrderId).HasColumnName("external_order_id");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.MarketplaceOrderId).HasColumnName("marketplace_order_id");
            entity.Property(e => e.NextAttemptAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("next_attempt_at");
            entity.Property(e => e.Payload)
                .HasColumnType("jsonb")
                .HasColumnName("payload");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'QUEUED'::text")
                .HasColumnName("status");
            entity.Property(e => e.TargetStatus).HasColumnName("target_status");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.MarketplaceOrder).WithMany(p => p.MarketplaceStatusSyncJobs)
                .HasForeignKey(d => d.MarketplaceOrderId)
                .HasConstraintName("marketplace_status_sync_jobs_marketplace_order_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.MarketplaceStatusSyncJobs)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("marketplace_status_sync_jobs_restaurant_id_fkey");
        });

        modelBuilder.Entity<MarketplaceWebhookEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("marketplace_webhook_events_pkey");

            entity.ToTable("marketplace_webhook_events");

            entity.HasIndex(e => new { e.Provider, e.ExternalEventId }, "marketplace_webhook_events_provider_external_event_id_key").IsUnique();

            entity.HasIndex(e => new { e.RestaurantId, e.Provider, e.ReceivedAt }, "marketplace_webhook_events_restaurant_id_provider_received__idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.ExternalEventId).HasColumnName("external_event_id");
            entity.Property(e => e.ExternalOrderId).HasColumnName("external_order_id");
            entity.Property(e => e.IntegrationId).HasColumnName("integration_id");
            entity.Property(e => e.Outcome)
                .HasDefaultValueSql("'RECEIVED'::text")
                .HasColumnName("outcome");
            entity.Property(e => e.Payload)
                .HasColumnType("jsonb")
                .HasColumnName("payload");
            entity.Property(e => e.PayloadHash).HasColumnName("payload_hash");
            entity.Property(e => e.ProcessedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("processed_at");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.ReceivedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("received_at");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");

            entity.HasOne(d => d.Integration).WithMany(p => p.MarketplaceWebhookEvents)
                .HasForeignKey(d => d.IntegrationId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("marketplace_webhook_events_integration_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.MarketplaceWebhookEvents)
                .HasForeignKey(d => d.RestaurantId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("marketplace_webhook_events_restaurant_id_fkey");
        });

        modelBuilder.Entity<MenuCategory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("menu_categories_pkey");

            entity.ToTable("menu_categories");

            entity.HasIndex(e => e.PrimaryCategoryId, "menu_categories_primary_category_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.ChannelVisibility)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("channel_visibility");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DescriptionEn).HasColumnName("description_en");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.Image).HasColumnName("image");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NameEn).HasColumnName("name_en");
            entity.Property(e => e.PrimaryCategoryId).HasColumnName("primary_category_id");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.PrimaryCategory).WithMany(p => p.MenuCategories)
                .HasForeignKey(d => d.PrimaryCategoryId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("menu_categories_primary_category_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.MenuCategories)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("menu_categories_restaurant_id_fkey");
        });

        modelBuilder.Entity<MenuItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("menu_items_pkey");

            entity.ToTable("menu_items");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AiConfidence).HasColumnName("ai_confidence");
            entity.Property(e => e.AiEstimatedCost)
                .HasPrecision(10, 2)
                .HasColumnName("ai_estimated_cost");
            entity.Property(e => e.AiLastUpdated)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("ai_last_updated");
            entity.Property(e => e.AiProfitMargin)
                .HasPrecision(5, 2)
                .HasColumnName("ai_profit_margin");
            entity.Property(e => e.AiSuggestedPrice)
                .HasPrecision(10, 2)
                .HasColumnName("ai_suggested_price");
            entity.Property(e => e.Allergens)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("allergens");
            entity.Property(e => e.Available)
                .HasDefaultValue(true)
                .HasColumnName("available");
            entity.Property(e => e.BeverageType).HasColumnName("beverage_type");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.CateringAllergens)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("catering_allergens");
            entity.Property(e => e.CateringPricing)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("catering_pricing");
            entity.Property(e => e.CateringPricingModel).HasColumnName("catering_pricing_model");
            entity.Property(e => e.ChannelVisibility)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("channel_visibility");
            entity.Property(e => e.Cost)
                .HasPrecision(10, 2)
                .HasColumnName("cost");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DescriptionEn).HasColumnName("description_en");
            entity.Property(e => e.Dietary)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("dietary");
            entity.Property(e => e.DietaryFlags)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("dietary_flags");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.EightySixReason).HasColumnName("eighty_six_reason");
            entity.Property(e => e.EightySixed).HasColumnName("eighty_sixed");
            entity.Property(e => e.Image).HasColumnName("image");
            entity.Property(e => e.ItemCategory).HasColumnName("item_category");
            entity.Property(e => e.MenuType)
                .HasDefaultValueSql("'standard'::text")
                .HasColumnName("menu_type");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NameEn).HasColumnName("name_en");
            entity.Property(e => e.Popular).HasColumnName("popular");
            entity.Property(e => e.PrepTimeMinutes).HasColumnName("prep_time_minutes");
            entity.Property(e => e.Price)
                .HasPrecision(10, 2)
                .HasColumnName("price");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.TaxCategory)
                .HasDefaultValueSql("'prepared_food'::text")
                .HasColumnName("tax_category");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.VendorId).HasColumnName("vendor_id");

            entity.HasOne(d => d.Category).WithMany(p => p.MenuItems)
                .HasForeignKey(d => d.CategoryId)
                .HasConstraintName("menu_items_category_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.MenuItems)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("menu_items_restaurant_id_fkey");
        });

        modelBuilder.Entity<MenuItemModifierGroup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("menu_item_modifier_groups_pkey");

            entity.ToTable("menu_item_modifier_groups");

            entity.HasIndex(e => new { e.MenuItemId, e.ModifierGroupId }, "menu_item_modifier_groups_menu_item_id_modifier_group_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.MenuItemId).HasColumnName("menu_item_id");
            entity.Property(e => e.ModifierGroupId).HasColumnName("modifier_group_id");

            entity.HasOne(d => d.MenuItem).WithMany(p => p.MenuItemModifierGroups)
                .HasForeignKey(d => d.MenuItemId)
                .HasConstraintName("menu_item_modifier_groups_menu_item_id_fkey");

            entity.HasOne(d => d.ModifierGroup).WithMany(p => p.MenuItemModifierGroups)
                .HasForeignKey(d => d.ModifierGroupId)
                .HasConstraintName("menu_item_modifier_groups_modifier_group_id_fkey");
        });

        modelBuilder.Entity<MenuSyncLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("menu_sync_logs_pkey");

            entity.ToTable("menu_sync_logs");

            entity.HasIndex(e => e.RestaurantGroupId, "menu_sync_logs_restaurant_group_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Conflicts).HasColumnName("conflicts");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.ItemsAdded).HasColumnName("items_added");
            entity.Property(e => e.ItemsSkipped).HasColumnName("items_skipped");
            entity.Property(e => e.ItemsUpdated).HasColumnName("items_updated");
            entity.Property(e => e.RestaurantGroupId).HasColumnName("restaurant_group_id");
            entity.Property(e => e.SourceRestaurantId).HasColumnName("source_restaurant_id");
            entity.Property(e => e.SyncedBy).HasColumnName("synced_by");
            entity.Property(e => e.TargetRestaurantIds)
                .HasColumnType("jsonb")
                .HasColumnName("target_restaurant_ids");

            entity.HasOne(d => d.RestaurantGroup).WithMany(p => p.MenuSyncLogs)
                .HasForeignKey(d => d.RestaurantGroupId)
                .HasConstraintName("menu_sync_logs_restaurant_group_id_fkey");
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("messages_pkey");

            entity.ToTable("messages");

            entity.HasIndex(e => e.ThreadId, "messages_thread_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Body).HasColumnName("body");
            entity.Property(e => e.Direction).HasColumnName("direction");
            entity.Property(e => e.SentAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("sent_at");
            entity.Property(e => e.ThreadId).HasColumnName("thread_id");

            entity.HasOne(d => d.Thread).WithMany(p => p.Messages)
                .HasForeignKey(d => d.ThreadId)
                .HasConstraintName("messages_thread_id_fkey");
        });

        modelBuilder.Entity<MessageTemplate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("message_templates_pkey");

            entity.ToTable("message_templates");

            entity.HasIndex(e => e.RestaurantId, "message_templates_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Body).HasColumnName("body");
            entity.Property(e => e.Channel).HasColumnName("channel");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Subject).HasColumnName("subject");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Variables)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("variables");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.MessageTemplates)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("message_templates_restaurant_id_fkey");
        });

        modelBuilder.Entity<MessageThread>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("message_threads_pkey");

            entity.ToTable("message_threads");

            entity.HasIndex(e => new { e.RestaurantId, e.CustomerId }, "message_threads_restaurant_id_customer_id_idx");

            entity.HasIndex(e => e.RestaurantId, "message_threads_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Channel).HasColumnName("channel");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.LastMessageAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_message_at");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Subject).HasColumnName("subject");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.MessageThreads)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("message_threads_restaurant_id_fkey");
        });

        modelBuilder.Entity<MfaAmrClaim>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("amr_id_pk");

            entity.ToTable("mfa_amr_claims", "auth", tb => tb.HasComment("auth: stores authenticator method reference claims for multi factor authentication"));

            entity.HasIndex(e => new { e.SessionId, e.AuthenticationMethod }, "mfa_amr_claims_session_id_authentication_method_pkey").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AuthenticationMethod).HasColumnName("authentication_method");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.Session).WithMany(p => p.MfaAmrClaims)
                .HasForeignKey(d => d.SessionId)
                .HasConstraintName("mfa_amr_claims_session_id_fkey");
        });

        modelBuilder.Entity<MfaChallenge>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("mfa_challenges_pkey");

            entity.ToTable("mfa_challenges", "auth", tb => tb.HasComment("auth: stores metadata about challenge requests made"));

            entity.HasIndex(e => e.CreatedAt, "mfa_challenge_created_at_idx").IsDescending();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.FactorId).HasColumnName("factor_id");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
            entity.Property(e => e.OtpCode).HasColumnName("otp_code");
            entity.Property(e => e.VerifiedAt).HasColumnName("verified_at");
            entity.Property(e => e.WebAuthnSessionData)
                .HasColumnType("jsonb")
                .HasColumnName("web_authn_session_data");

            entity.HasOne(d => d.Factor).WithMany(p => p.MfaChallenges)
                .HasForeignKey(d => d.FactorId)
                .HasConstraintName("mfa_challenges_auth_factor_id_fkey");
        });

        modelBuilder.Entity<MfaFactor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("mfa_factors_pkey");

            entity.ToTable("mfa_factors", "auth", tb => tb.HasComment("auth: stores metadata about factors"));

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "factor_id_created_at_idx");

            entity.HasIndex(e => e.LastChallengedAt, "mfa_factors_last_challenged_at_key").IsUnique();

            entity.HasIndex(e => new { e.FriendlyName, e.UserId }, "mfa_factors_user_friendly_name_unique")
                .IsUnique()
                .HasFilter("(TRIM(BOTH FROM friendly_name) <> ''::text)");

            entity.HasIndex(e => e.UserId, "mfa_factors_user_id_idx");

            entity.HasIndex(e => new { e.UserId, e.Phone }, "unique_phone_factor_per_user").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.FriendlyName).HasColumnName("friendly_name");
            entity.Property(e => e.LastChallengedAt).HasColumnName("last_challenged_at");
            entity.Property(e => e.LastWebauthnChallengeData)
                .HasComment("Stores the latest WebAuthn challenge data including attestation/assertion for customer verification")
                .HasColumnType("jsonb")
                .HasColumnName("last_webauthn_challenge_data");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.Secret).HasColumnName("secret");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WebAuthnAaguid).HasColumnName("web_authn_aaguid");
            entity.Property(e => e.WebAuthnCredential)
                .HasColumnType("jsonb")
                .HasColumnName("web_authn_credential");

            entity.HasOne(d => d.User).WithMany(p => p.MfaFactors)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("mfa_factors_user_id_fkey");
        });

        modelBuilder.Entity<MfaSecret>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("mfa_secrets_pkey");

            entity.ToTable("mfa_secrets");

            entity.HasIndex(e => e.TeamMemberId, "mfa_secrets_team_member_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BackupCodes).HasColumnName("backup_codes");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EmailOtpExpiry)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("email_otp_expiry");
            entity.Property(e => e.EmailOtpHash).HasColumnName("email_otp_hash");
            entity.Property(e => e.MfaType)
                .HasDefaultValueSql("'email'::text")
                .HasColumnName("mfa_type");
            entity.Property(e => e.Secret).HasColumnName("secret");
            entity.Property(e => e.TeamMemberId).HasColumnName("team_member_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Verified).HasColumnName("verified");

            entity.HasOne(d => d.TeamMember).WithOne(p => p.MfaSecret)
                .HasForeignKey<MfaSecret>(d => d.TeamMemberId)
                .HasConstraintName("mfa_secrets_team_member_id_fkey");
        });

        modelBuilder.Entity<MfaTrustedDevice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("mfa_trusted_devices_pkey");

            entity.ToTable("mfa_trusted_devices");

            entity.HasIndex(e => new { e.TeamMemberId, e.UaFingerprint }, "mfa_trusted_devices_team_member_id_ua_fingerprint_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DeviceInfo).HasColumnName("device_info");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.TeamMemberId).HasColumnName("team_member_id");
            entity.Property(e => e.TrustedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("trusted_at");
            entity.Property(e => e.UaFingerprint).HasColumnName("ua_fingerprint");

            entity.HasOne(d => d.TeamMember).WithMany(p => p.MfaTrustedDevices)
                .HasForeignKey(d => d.TeamMemberId)
                .HasConstraintName("mfa_trusted_devices_team_member_id_fkey");
        });

        modelBuilder.Entity<Migration>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("migrations_pkey");

            entity.ToTable("migrations", "storage");

            entity.HasIndex(e => e.Name, "migrations_name_key").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ExecutedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("executed_at");
            entity.Property(e => e.Hash)
                .HasMaxLength(40)
                .HasColumnName("hash");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Modifier>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("modifiers_pkey");

            entity.ToTable("modifiers");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Available)
                .HasDefaultValue(true)
                .HasColumnName("available");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.IsDefault).HasColumnName("is_default");
            entity.Property(e => e.ModifierGroupId).HasColumnName("modifier_group_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NameEn).HasColumnName("name_en");
            entity.Property(e => e.PriceAdjustment)
                .HasPrecision(10, 2)
                .HasColumnName("price_adjustment");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ModifierGroup).WithMany(p => p.Modifiers)
                .HasForeignKey(d => d.ModifierGroupId)
                .HasConstraintName("modifiers_modifier_group_id_fkey");
        });

        modelBuilder.Entity<ModifierGroup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("modifier_groups_pkey");

            entity.ToTable("modifier_groups");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DescriptionEn).HasColumnName("description_en");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.MaxSelections).HasColumnName("max_selections");
            entity.Property(e => e.MinSelections).HasColumnName("min_selections");
            entity.Property(e => e.MultiSelect).HasColumnName("multi_select");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NameEn).HasColumnName("name_en");
            entity.Property(e => e.Required).HasColumnName("required");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.ModifierGroups)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("modifier_groups_restaurant_id_fkey");
        });

        modelBuilder.Entity<OauthAuthorization>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("oauth_authorizations_pkey");

            entity.ToTable("oauth_authorizations", "auth");

            entity.HasIndex(e => e.ExpiresAt, "oauth_auth_pending_exp_idx").HasFilter("(status = 'pending'::auth.oauth_authorization_status)");

            entity.HasIndex(e => e.AuthorizationCode, "oauth_authorizations_authorization_code_key").IsUnique();

            entity.HasIndex(e => e.AuthorizationId, "oauth_authorizations_authorization_id_key").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ApprovedAt).HasColumnName("approved_at");
            entity.Property(e => e.AuthorizationCode).HasColumnName("authorization_code");
            entity.Property(e => e.AuthorizationId).HasColumnName("authorization_id");
            entity.Property(e => e.ClientId).HasColumnName("client_id");
            entity.Property(e => e.CodeChallenge).HasColumnName("code_challenge");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt)
                .HasDefaultValueSql("(now() + '00:03:00'::interval)")
                .HasColumnName("expires_at");
            entity.Property(e => e.Nonce).HasColumnName("nonce");
            entity.Property(e => e.RedirectUri).HasColumnName("redirect_uri");
            entity.Property(e => e.Resource).HasColumnName("resource");
            entity.Property(e => e.Scope).HasColumnName("scope");
            entity.Property(e => e.State).HasColumnName("state");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Client).WithMany(p => p.OauthAuthorizations)
                .HasForeignKey(d => d.ClientId)
                .HasConstraintName("oauth_authorizations_client_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.OauthAuthorizations)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("oauth_authorizations_user_id_fkey");
        });

        modelBuilder.Entity<OauthClient>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("OAuthClient_pkey");

            entity.ToTable("OAuthClient", "geek_auth");

            entity.HasIndex(e => e.ClientId, "OAuthClient_clientId_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ClientId).HasColumnName("clientId");
            entity.Property(e => e.ClientSecret).HasColumnName("clientSecret");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.GrantTypes)
                .HasColumnType("jsonb")
                .HasColumnName("grantTypes");
            entity.Property(e => e.RedirectUris)
                .HasColumnType("jsonb")
                .HasColumnName("redirectUris");
            entity.Property(e => e.ResponseTypes)
                .HasColumnType("jsonb")
                .HasColumnName("responseTypes");
            entity.Property(e => e.Scope).HasColumnName("scope");
            entity.Property(e => e.TokenEndpointAuthMethod).HasColumnName("tokenEndpointAuthMethod");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updatedAt");
        });

        modelBuilder.Entity<OauthClient1>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("oauth_clients_pkey");

            entity.ToTable("oauth_clients", "auth");

            entity.HasIndex(e => e.DeletedAt, "oauth_clients_deleted_at_idx");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ClientName).HasColumnName("client_name");
            entity.Property(e => e.ClientSecretHash).HasColumnName("client_secret_hash");
            entity.Property(e => e.ClientUri).HasColumnName("client_uri");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.GrantTypes).HasColumnName("grant_types");
            entity.Property(e => e.LogoUri).HasColumnName("logo_uri");
            entity.Property(e => e.RedirectUris).HasColumnName("redirect_uris");
            entity.Property(e => e.TokenEndpointAuthMethod).HasColumnName("token_endpoint_auth_method");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<OauthClientState>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("oauth_client_states_pkey");

            entity.ToTable("oauth_client_states", "auth", tb => tb.HasComment("Stores OAuth states for third-party provider authentication flows where Supabase acts as the OAuth client."));

            entity.HasIndex(e => e.CreatedAt, "idx_oauth_client_states_created_at");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CodeVerifier).HasColumnName("code_verifier");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ProviderType).HasColumnName("provider_type");
        });

        modelBuilder.Entity<OauthConsent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("oauth_consents_pkey");

            entity.ToTable("oauth_consents", "auth");

            entity.HasIndex(e => e.ClientId, "oauth_consents_active_client_idx").HasFilter("(revoked_at IS NULL)");

            entity.HasIndex(e => new { e.UserId, e.ClientId }, "oauth_consents_active_user_client_idx").HasFilter("(revoked_at IS NULL)");

            entity.HasIndex(e => new { e.UserId, e.ClientId }, "oauth_consents_user_client_unique").IsUnique();

            entity.HasIndex(e => new { e.UserId, e.GrantedAt }, "oauth_consents_user_order_idx").IsDescending(false, true);

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ClientId).HasColumnName("client_id");
            entity.Property(e => e.GrantedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("granted_at");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.Scopes).HasColumnName("scopes");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Client).WithMany(p => p.OauthConsents)
                .HasForeignKey(d => d.ClientId)
                .HasConstraintName("oauth_consents_client_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.OauthConsents)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("oauth_consents_user_id_fkey");
        });

        modelBuilder.Entity<OauthToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("OAuthToken_pkey");

            entity.ToTable("OAuthToken", "geek_auth");

            entity.HasIndex(e => e.AccessToken, "OAuthToken_accessToken_key").IsUnique();

            entity.HasIndex(e => e.RefreshToken, "OAuthToken_refreshToken_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AccessToken).HasColumnName("accessToken");
            entity.Property(e => e.AccessTokenExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("accessTokenExpiresAt");
            entity.Property(e => e.BiosId).HasColumnName("biosId");
            entity.Property(e => e.ClientId).HasColumnName("clientId");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("isActive");
            entity.Property(e => e.RefreshToken).HasColumnName("refreshToken");
            entity.Property(e => e.RefreshTokenExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("refreshTokenExpiresAt");
            entity.Property(e => e.Scope).HasColumnName("scope");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.OauthTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("OAuthToken_userId_fkey");
        });

        modelBuilder.Entity<Object>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("objects_pkey");

            entity.ToTable("objects", "storage");

            entity.HasIndex(e => new { e.BucketId, e.Name }, "bucketid_objname").IsUnique();

            entity.HasIndex(e => new { e.BucketId, e.Name }, "idx_objects_bucket_id_name").UseCollation(new[] { null, "C" });

            entity.HasIndex(e => e.Name, "name_prefix_search").HasOperators(new[] { "text_pattern_ops" });

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.BucketId).HasColumnName("bucket_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.LastAccessedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("last_accessed_at");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Owner)
                .HasComment("Field is deprecated, use owner_id instead")
                .HasColumnName("owner");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.PathTokens)
                .HasComputedColumnSql("string_to_array(name, '/'::text)", true)
                .HasColumnName("path_tokens");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserMetadata)
                .HasColumnType("jsonb")
                .HasColumnName("user_metadata");
            entity.Property(e => e.Version).HasColumnName("version");

            entity.HasOne(d => d.Bucket).WithMany(p => p.Objects)
                .HasForeignKey(d => d.BucketId)
                .HasConstraintName("objects_bucketId_fkey");
        });

        modelBuilder.Entity<OidcStorage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("OidcStorage_pkey");

            entity.ToTable("OidcStorage", "geek_auth");

            entity.HasIndex(e => e.GrantId, "OidcStorage_grantId_idx");

            entity.HasIndex(e => e.TokenHash, "OidcStorage_tokenHash_key").IsUnique();

            entity.HasIndex(e => e.Uid, "OidcStorage_uid_key").IsUnique();

            entity.HasIndex(e => e.UserCode, "OidcStorage_userCode_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expiresAt");
            entity.Property(e => e.GrantId).HasColumnName("grantId");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.Payload)
                .HasColumnType("jsonb")
                .HasColumnName("payload");
            entity.Property(e => e.TokenHash).HasColumnName("tokenHash");
            entity.Property(e => e.Uid).HasColumnName("uid");
            entity.Property(e => e.UserCode).HasColumnName("userCode");
        });

        modelBuilder.Entity<OneTimeToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("one_time_tokens_pkey");

            entity.ToTable("one_time_tokens", "auth");

            entity.HasIndex(e => e.RelatesTo, "one_time_tokens_relates_to_hash_idx").HasMethod("hash");

            entity.HasIndex(e => e.TokenHash, "one_time_tokens_token_hash_hash_idx").HasMethod("hash");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.RelatesTo).HasColumnName("relates_to");
            entity.Property(e => e.TokenHash).HasColumnName("token_hash");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.OneTimeTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("one_time_tokens_user_id_fkey");
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("orders_pkey");

            entity.ToTable("orders");

            entity.HasIndex(e => e.OrderNumber, "orders_order_number_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ApprovalStatus).HasColumnName("approval_status");
            entity.Property(e => e.ApprovedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("approved_at");
            entity.Property(e => e.ApprovedBy).HasColumnName("approved_by");
            entity.Property(e => e.ArrivalNotified).HasColumnName("arrival_notified");
            entity.Property(e => e.CancellationReason).HasColumnName("cancellation_reason");
            entity.Property(e => e.CancelledAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("cancelled_at");
            entity.Property(e => e.CancelledBy).HasColumnName("cancelled_by");
            entity.Property(e => e.CateringInstructions).HasColumnName("catering_instructions");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.ConfirmedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("confirmed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.DeliveredAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("delivered_at");
            entity.Property(e => e.DeliveryAddress).HasColumnName("delivery_address");
            entity.Property(e => e.DeliveryAddress2).HasColumnName("delivery_address_2");
            entity.Property(e => e.DeliveryCity).HasColumnName("delivery_city");
            entity.Property(e => e.DeliveryEstimatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("delivery_estimated_at");
            entity.Property(e => e.DeliveryExternalId).HasColumnName("delivery_external_id");
            entity.Property(e => e.DeliveryFee)
                .HasPrecision(10, 2)
                .HasColumnName("delivery_fee");
            entity.Property(e => e.DeliveryLat)
                .HasPrecision(10, 7)
                .HasColumnName("delivery_lat");
            entity.Property(e => e.DeliveryLng)
                .HasPrecision(10, 7)
                .HasColumnName("delivery_lng");
            entity.Property(e => e.DeliveryNotes).HasColumnName("delivery_notes");
            entity.Property(e => e.DeliveryProvider).HasColumnName("delivery_provider");
            entity.Property(e => e.DeliveryStateUs).HasColumnName("delivery_state_us");
            entity.Property(e => e.DeliveryStatus).HasColumnName("delivery_status");
            entity.Property(e => e.DeliveryTrackingUrl).HasColumnName("delivery_tracking_url");
            entity.Property(e => e.DeliveryZip).HasColumnName("delivery_zip");
            entity.Property(e => e.DepositAmount)
                .HasPrecision(10, 2)
                .HasColumnName("deposit_amount");
            entity.Property(e => e.DepositPaid).HasColumnName("deposit_paid");
            entity.Property(e => e.Discount)
                .HasPrecision(10, 2)
                .HasColumnName("discount");
            entity.Property(e => e.DispatchStatus).HasColumnName("dispatch_status");
            entity.Property(e => e.DispatchedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("dispatched_at");
            entity.Property(e => e.EventDate)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("event_date");
            entity.Property(e => e.EventTime).HasColumnName("event_time");
            entity.Property(e => e.EventType).HasColumnName("event_type");
            entity.Property(e => e.Headcount).HasColumnName("headcount");
            entity.Property(e => e.LoyaltyPointsEarned).HasColumnName("loyalty_points_earned");
            entity.Property(e => e.LoyaltyPointsRedeemed).HasColumnName("loyalty_points_redeemed");
            entity.Property(e => e.OrderNumber).HasColumnName("order_number");
            entity.Property(e => e.OrderSource)
                .HasDefaultValueSql("'online'::text")
                .HasColumnName("order_source");
            entity.Property(e => e.OrderType).HasColumnName("order_type");
            entity.Property(e => e.PaymentMethod).HasColumnName("payment_method");
            entity.Property(e => e.PaymentStatus)
                .HasDefaultValueSql("'pending'::text")
                .HasColumnName("payment_status");
            entity.Property(e => e.PaypalCaptureId).HasColumnName("paypal_capture_id");
            entity.Property(e => e.PaypalOrderId).HasColumnName("paypal_order_id");
            entity.Property(e => e.PreparingAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("preparing_at");
            entity.Property(e => e.ReadyAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("ready_at");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.ScheduledTime)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("scheduled_time");
            entity.Property(e => e.SentToKitchenAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("sent_to_kitchen_at");
            entity.Property(e => e.ServerId).HasColumnName("server_id");
            entity.Property(e => e.SetupRequired).HasColumnName("setup_required");
            entity.Property(e => e.SourceDeviceId).HasColumnName("source_device_id");
            entity.Property(e => e.SpecialInstructions).HasColumnName("special_instructions");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'::text")
                .HasColumnName("status");
            entity.Property(e => e.StripePaymentIntentId).HasColumnName("stripe_payment_intent_id");
            entity.Property(e => e.Subtotal)
                .HasPrecision(10, 2)
                .HasColumnName("subtotal");
            entity.Property(e => e.TableId).HasColumnName("table_id");
            entity.Property(e => e.Tax)
                .HasPrecision(10, 2)
                .HasColumnName("tax");
            entity.Property(e => e.ThrottleHeldAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("throttle_held_at");
            entity.Property(e => e.ThrottleReason).HasColumnName("throttle_reason");
            entity.Property(e => e.ThrottleReleaseReason).HasColumnName("throttle_release_reason");
            entity.Property(e => e.ThrottleReleasedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("throttle_released_at");
            entity.Property(e => e.ThrottleSource).HasColumnName("throttle_source");
            entity.Property(e => e.ThrottleState)
                .HasDefaultValueSql("'NONE'::text")
                .HasColumnName("throttle_state");
            entity.Property(e => e.Tip)
                .HasPrecision(10, 2)
                .HasColumnName("tip");
            entity.Property(e => e.Total)
                .HasPrecision(10, 2)
                .HasColumnName("total");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.VehicleDescription).HasColumnName("vehicle_description");

            entity.HasOne(d => d.Customer).WithMany(p => p.Orders)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("orders_customer_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Orders)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("orders_restaurant_id_fkey");

            entity.HasOne(d => d.Table).WithMany(p => p.Orders)
                .HasForeignKey(d => d.TableId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("orders_table_id_fkey");
        });

        modelBuilder.Entity<OrderCheck>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("order_checks_pkey");

            entity.ToTable("order_checks");

            entity.HasIndex(e => e.OrderId, "order_checks_order_id_idx");

            entity.HasIndex(e => e.RestaurantId, "order_checks_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DisplayNumber).HasColumnName("display_number");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.PaymentStatus)
                .HasDefaultValueSql("'OPEN'::text")
                .HasColumnName("payment_status");
            entity.Property(e => e.PreauthId).HasColumnName("preauth_id");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Subtotal)
                .HasPrecision(10, 2)
                .HasColumnName("subtotal");
            entity.Property(e => e.TabClosedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("tab_closed_at");
            entity.Property(e => e.TabName).HasColumnName("tab_name");
            entity.Property(e => e.TabOpenedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("tab_opened_at");
            entity.Property(e => e.Tax)
                .HasPrecision(10, 2)
                .HasColumnName("tax");
            entity.Property(e => e.Tip)
                .HasPrecision(10, 2)
                .HasColumnName("tip");
            entity.Property(e => e.Total)
                .HasPrecision(10, 2)
                .HasColumnName("total");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Order).WithMany(p => p.OrderChecks)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("order_checks_order_id_fkey");
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("order_items_pkey");

            entity.ToTable("order_items");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.CourseFireStatus).HasColumnName("course_fire_status");
            entity.Property(e => e.CourseFiredAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("course_fired_at");
            entity.Property(e => e.CourseGuid).HasColumnName("course_guid");
            entity.Property(e => e.CourseName).HasColumnName("course_name");
            entity.Property(e => e.CourseReadyAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("course_ready_at");
            entity.Property(e => e.CourseSortOrder).HasColumnName("course_sort_order");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.FulfillmentStatus)
                .HasDefaultValueSql("'NEW'::text")
                .HasColumnName("fulfillment_status");
            entity.Property(e => e.MenuItemId).HasColumnName("menu_item_id");
            entity.Property(e => e.MenuItemName).HasColumnName("menu_item_name");
            entity.Property(e => e.ModifiersPrice)
                .HasPrecision(10, 2)
                .HasColumnName("modifiers_price");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.SentToKitchenAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("sent_to_kitchen_at");
            entity.Property(e => e.SpecialInstructions).HasColumnName("special_instructions");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'::text")
                .HasColumnName("status");
            entity.Property(e => e.TotalPrice)
                .HasPrecision(10, 2)
                .HasColumnName("total_price");
            entity.Property(e => e.UnitPrice)
                .HasPrecision(10, 2)
                .HasColumnName("unit_price");

            entity.HasOne(d => d.MenuItem).WithMany(p => p.OrderItems)
                .HasForeignKey(d => d.MenuItemId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("order_items_menu_item_id_fkey");

            entity.HasOne(d => d.Order).WithMany(p => p.OrderItems)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("order_items_order_id_fkey");
        });

        modelBuilder.Entity<OrderItemModifier>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("order_item_modifiers_pkey");

            entity.ToTable("order_item_modifiers");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ModifierId).HasColumnName("modifier_id");
            entity.Property(e => e.ModifierName).HasColumnName("modifier_name");
            entity.Property(e => e.OrderItemId).HasColumnName("order_item_id");
            entity.Property(e => e.PriceAdjustment)
                .HasPrecision(10, 2)
                .HasColumnName("price_adjustment");

            entity.HasOne(d => d.Modifier).WithMany(p => p.OrderItemModifiers)
                .HasForeignKey(d => d.ModifierId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("order_item_modifiers_modifier_id_fkey");

            entity.HasOne(d => d.OrderItem).WithMany(p => p.OrderItemModifiers)
                .HasForeignKey(d => d.OrderItemId)
                .HasConstraintName("order_item_modifiers_order_item_id_fkey");
        });

        modelBuilder.Entity<OrderSentiment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("order_sentiments_pkey");

            entity.ToTable("order_sentiments");

            entity.HasIndex(e => e.OrderId, "order_sentiments_order_id_key").IsUnique();

            entity.HasIndex(e => e.RestaurantId, "order_sentiments_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.IsRead }, "order_sentiments_restaurant_id_is_read_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Urgency }, "order_sentiments_restaurant_id_urgency_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AnalyzedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("analyzed_at");
            entity.Property(e => e.Flags).HasColumnName("flags");
            entity.Property(e => e.IsRead).HasColumnName("is_read");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.OrderNumber).HasColumnName("order_number");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Sentiment).HasColumnName("sentiment");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.Property(e => e.TableNumber).HasColumnName("table_number");
            entity.Property(e => e.Urgency)
                .HasDefaultValueSql("'low'::text")
                .HasColumnName("urgency");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.OrderSentiments)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("order_sentiments_restaurant_id_fkey");
        });

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("order_status_history_pkey");

            entity.ToTable("order_status_history");

            entity.HasIndex(e => e.CreatedAt, "order_status_history_created_at_idx");

            entity.HasIndex(e => e.OrderId, "order_status_history_order_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ChangedBy).HasColumnName("changed_by");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.FromStatus).HasColumnName("from_status");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.ToStatus).HasColumnName("to_status");

            entity.HasOne(d => d.Order).WithMany(p => p.OrderStatusHistories)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("order_status_history_order_id_fkey");
        });

        modelBuilder.Entity<OrderTemplate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("order_templates_pkey");

            entity.ToTable("order_templates");

            entity.HasIndex(e => e.RestaurantId, "order_templates_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy)
                .HasDefaultValueSql("'system'::text")
                .HasColumnName("created_by");
            entity.Property(e => e.Items)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("items");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.OrderTemplates)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("order_templates_restaurant_id_fkey");
        });

        modelBuilder.Entity<PasswordHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("password_history_pkey");

            entity.ToTable("password_history");

            entity.HasIndex(e => new { e.TeamMemberId, e.CreatedAt }, "password_history_team_member_id_created_at_idx").IsDescending(false, true);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.TeamMemberId).HasColumnName("team_member_id");

            entity.HasOne(d => d.TeamMember).WithMany(p => p.PasswordHistories)
                .HasForeignKey(d => d.TeamMemberId)
                .HasConstraintName("password_history_team_member_id_fkey");
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("password_reset_tokens_pkey");

            entity.ToTable("password_reset_tokens");

            entity.HasIndex(e => e.TeamMemberId, "password_reset_tokens_team_member_id_idx");

            entity.HasIndex(e => e.Token, "password_reset_tokens_token_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.TeamMemberId).HasColumnName("team_member_id");
            entity.Property(e => e.Token).HasColumnName("token");
            entity.Property(e => e.UsedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("used_at");

            entity.HasOne(d => d.TeamMember).WithMany(p => p.PasswordResetTokens)
                .HasForeignKey(d => d.TeamMemberId)
                .HasConstraintName("password_reset_tokens_team_member_id_fkey");
        });

        modelBuilder.Entity<PayrollPeriod>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("payroll_periods_pkey");

            entity.ToTable("payroll_periods");

            entity.HasIndex(e => e.RestaurantId, "payroll_periods_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'draft'::text")
                .HasColumnName("status");
            entity.Property(e => e.Summaries)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("summaries");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.PayrollPeriods)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("payroll_periods_restaurant_id_fkey");
        });

        modelBuilder.Entity<PendingVerification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PendingVerification_pkey");

            entity.ToTable("PendingVerification", "geek_auth");

            entity.HasIndex(e => e.Email, "PendingVerification_email_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Attempts).HasColumnName("attempts");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expiresAt");
            entity.Property(e => e.OtpHash).HasColumnName("otpHash");
        });

        modelBuilder.Entity<PendingVerification1>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pending_verifications_pkey");

            entity.ToTable("pending_verifications");

            entity.HasIndex(e => e.EmailHash, "pending_verifications_email_hash_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.EmailHash).HasColumnName("email_hash");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.OtpHash).HasColumnName("otp_hash");
        });

        modelBuilder.Entity<PeripheralDevice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("peripheral_devices_pkey");

            entity.ToTable("peripheral_devices");

            entity.HasIndex(e => e.ParentDeviceId, "peripheral_devices_parent_device_id_idx");

            entity.HasIndex(e => e.RestaurantId, "peripheral_devices_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ConnectionType).HasColumnName("connection_type");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.LastSeenAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_seen_at");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ParentDeviceId).HasColumnName("parent_device_id");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'disconnected'::text")
                .HasColumnName("status");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ParentDevice).WithMany(p => p.PeripheralDevices)
                .HasForeignKey(d => d.ParentDeviceId)
                .HasConstraintName("peripheral_devices_parent_device_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.PeripheralDevices)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("peripheral_devices_restaurant_id_fkey");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Permission_pkey");

            entity.ToTable("Permission", "geek_auth");

            entity.HasIndex(e => e.Name, "Permission_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
        });

        modelBuilder.Entity<PermissionSet>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("permission_sets_pkey");

            entity.ToTable("permission_sets");

            entity.HasIndex(e => e.RestaurantId, "permission_sets_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsDefault).HasColumnName("is_default");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Permissions)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("permissions");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.PermissionSets)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("permission_sets_restaurant_id_fkey");
        });

        modelBuilder.Entity<PrimaryCategory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("primary_categories_pkey");

            entity.ToTable("primary_categories");

            entity.HasIndex(e => e.RestaurantId, "primary_categories_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Slug }, "primary_categories_restaurant_id_slug_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.Icon)
                .HasMaxLength(50)
                .HasColumnName("icon");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.NameEn)
                .HasMaxLength(100)
                .HasColumnName("name_en");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Slug)
                .HasMaxLength(50)
                .HasColumnName("slug");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.PrimaryCategories)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("primary_categories_restaurant_id_fkey");
        });

        modelBuilder.Entity<PrintJob>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("print_jobs_pkey");

            entity.ToTable("print_jobs");

            entity.HasIndex(e => e.OrderId, "print_jobs_order_id_idx");

            entity.HasIndex(e => new { e.PrinterId, e.Status }, "print_jobs_printer_id_status_idx");

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "print_jobs_status_created_at_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
            entity.Property(e => e.CompletedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.JobData)
                .HasColumnType("jsonb")
                .HasColumnName("job_data");
            entity.Property(e => e.OrderId).HasColumnName("order_id");
            entity.Property(e => e.PrinterId).HasColumnName("printer_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'::text")
                .HasColumnName("status");

            entity.HasOne(d => d.Order).WithMany(p => p.PrintJobs)
                .HasForeignKey(d => d.OrderId)
                .HasConstraintName("print_jobs_order_id_fkey");

            entity.HasOne(d => d.Printer).WithMany(p => p.PrintJobs)
                .HasForeignKey(d => d.PrinterId)
                .HasConstraintName("print_jobs_printer_id_fkey");
        });

        modelBuilder.Entity<Printer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("printers_pkey");

            entity.ToTable("printers");

            entity.HasIndex(e => e.CloudprntId, "printers_cloudprnt_id_key").IsUnique();

            entity.HasIndex(e => e.MacAddress, "printers_mac_address_idx");

            entity.HasIndex(e => e.MacAddress, "printers_mac_address_key").IsUnique();

            entity.HasIndex(e => e.RegistrationToken, "printers_registration_token_key").IsUnique();

            entity.HasIndex(e => e.RestaurantId, "printers_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.IsDefault }, "printers_restaurant_id_is_default_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CloudprntId).HasColumnName("cloudprnt_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsDefault).HasColumnName("is_default");
            entity.Property(e => e.LastPollAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_poll_at");
            entity.Property(e => e.MacAddress).HasColumnName("mac_address");
            entity.Property(e => e.Model).HasColumnName("model");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PrintWidth)
                .HasDefaultValue(48)
                .HasColumnName("print_width");
            entity.Property(e => e.RegistrationToken).HasColumnName("registration_token");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Printers)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("printers_restaurant_id_fkey");
        });

        modelBuilder.Entity<PrinterProfile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("printer_profiles_pkey");

            entity.ToTable("printer_profiles");

            entity.HasIndex(e => e.RestaurantId, "printer_profiles_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Name }, "printer_profiles_restaurant_id_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsDefault).HasColumnName("is_default");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.RoutingRules)
                .HasColumnType("jsonb")
                .HasColumnName("routingRules");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.PrinterProfiles)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("printer_profiles_restaurant_id_fkey");
        });

        modelBuilder.Entity<PrismaMigration>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("_prisma_migrations_pkey");

            entity.ToTable("_prisma_migrations");

            entity.Property(e => e.Id)
                .HasMaxLength(36)
                .HasColumnName("id");
            entity.Property(e => e.AppliedStepsCount).HasColumnName("applied_steps_count");
            entity.Property(e => e.Checksum)
                .HasMaxLength(64)
                .HasColumnName("checksum");
            entity.Property(e => e.FinishedAt).HasColumnName("finished_at");
            entity.Property(e => e.Logs).HasColumnName("logs");
            entity.Property(e => e.MigrationName)
                .HasMaxLength(255)
                .HasColumnName("migration_name");
            entity.Property(e => e.RolledBackAt).HasColumnName("rolled_back_at");
            entity.Property(e => e.StartedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("started_at");
        });

        modelBuilder.Entity<PtoRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pto_requests_pkey");

            entity.ToTable("pto_requests");

            entity.HasIndex(e => new { e.RestaurantId, e.Status }, "pto_requests_restaurant_id_status_idx");

            entity.HasIndex(e => e.StaffPinId, "pto_requests_staff_pin_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.EndDate)
                .HasMaxLength(10)
                .HasColumnName("end_date");
            entity.Property(e => e.HoursRequested).HasColumnName("hours_requested");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.ReviewedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("reviewed_at");
            entity.Property(e => e.ReviewedBy).HasColumnName("reviewed_by");
            entity.Property(e => e.StaffPinId).HasColumnName("staff_pin_id");
            entity.Property(e => e.StartDate)
                .HasMaxLength(10)
                .HasColumnName("start_date");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'::text")
                .HasColumnName("status");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.StaffPin).WithMany(p => p.PtoRequests)
                .HasForeignKey(d => d.StaffPinId)
                .HasConstraintName("pto_requests_staff_pin_id_fkey");
        });

        modelBuilder.Entity<PurchaseInvoice>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("purchase_invoices_pkey");

            entity.ToTable("purchase_invoices");

            entity.HasIndex(e => e.RestaurantId, "purchase_invoices_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.InvoiceNumber }, "purchase_invoices_restaurant_id_invoice_number_key").IsUnique();

            entity.HasIndex(e => e.VendorId, "purchase_invoices_vendor_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.ImageUrl).HasColumnName("image_url");
            entity.Property(e => e.InvoiceDate)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("invoice_date");
            entity.Property(e => e.InvoiceNumber).HasColumnName("invoice_number");
            entity.Property(e => e.OcrProcessedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("ocr_processed_at");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending_review'::text")
                .HasColumnName("status");
            entity.Property(e => e.TotalAmount)
                .HasPrecision(10, 2)
                .HasColumnName("total_amount");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.VendorId).HasColumnName("vendor_id");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.PurchaseInvoices)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("purchase_invoices_restaurant_id_fkey");

            entity.HasOne(d => d.Vendor).WithMany(p => p.PurchaseInvoices)
                .HasForeignKey(d => d.VendorId)
                .HasConstraintName("purchase_invoices_vendor_id_fkey");
        });

        modelBuilder.Entity<PurchaseLineItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("purchase_line_items_pkey");

            entity.ToTable("purchase_line_items");

            entity.HasIndex(e => e.InvoiceId, "purchase_line_items_invoice_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IngredientName).HasColumnName("ingredient_name");
            entity.Property(e => e.InvoiceId).HasColumnName("invoice_id");
            entity.Property(e => e.NormalizedIngredient).HasColumnName("normalized_ingredient");
            entity.Property(e => e.Quantity)
                .HasPrecision(10, 4)
                .HasColumnName("quantity");
            entity.Property(e => e.TotalCost)
                .HasPrecision(10, 2)
                .HasColumnName("total_cost");
            entity.Property(e => e.Unit).HasColumnName("unit");
            entity.Property(e => e.UnitCost)
                .HasPrecision(10, 4)
                .HasColumnName("unit_cost");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Invoice).WithMany(p => p.PurchaseLineItems)
                .HasForeignKey(d => d.InvoiceId)
                .HasConstraintName("purchase_line_items_invoice_id_fkey");
        });

        modelBuilder.Entity<RecipeIngredient>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("recipe_ingredients_pkey");

            entity.ToTable("recipe_ingredients");

            entity.HasIndex(e => e.InventoryItemId, "recipe_ingredients_inventory_item_id_idx");

            entity.HasIndex(e => e.MenuItemId, "recipe_ingredients_menu_item_id_idx");

            entity.HasIndex(e => new { e.MenuItemId, e.InventoryItemId }, "recipe_ingredients_menu_item_id_inventory_item_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.InventoryItemId).HasColumnName("inventory_item_id");
            entity.Property(e => e.MenuItemId).HasColumnName("menu_item_id");
            entity.Property(e => e.Notes)
                .HasMaxLength(255)
                .HasColumnName("notes");
            entity.Property(e => e.Quantity)
                .HasPrecision(10, 4)
                .HasColumnName("quantity");
            entity.Property(e => e.Unit)
                .HasMaxLength(50)
                .HasColumnName("unit");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.InventoryItem).WithMany(p => p.RecipeIngredients)
                .HasForeignKey(d => d.InventoryItemId)
                .HasConstraintName("recipe_ingredients_inventory_item_id_fkey");

            entity.HasOne(d => d.MenuItem).WithMany(p => p.RecipeIngredients)
                .HasForeignKey(d => d.MenuItemId)
                .HasConstraintName("recipe_ingredients_menu_item_id_fkey");
        });

        modelBuilder.Entity<RecurringReservation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("recurring_reservations_pkey");

            entity.ToTable("recurring_reservations");

            entity.HasIndex(e => e.RestaurantId, "recurring_reservations_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.CustomerName).HasColumnName("customer_name");
            entity.Property(e => e.CustomerPhone).HasColumnName("customer_phone");
            entity.Property(e => e.DayOfWeek).HasColumnName("day_of_week");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.PartySize).HasColumnName("party_size");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Time)
                .HasMaxLength(5)
                .HasColumnName("time");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.RecurringReservations)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("recurring_reservations_restaurant_id_fkey");
        });

        modelBuilder.Entity<ReferralConfig>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("referral_configs_pkey");

            entity.ToTable("referral_configs");

            entity.HasIndex(e => e.RestaurantId, "referral_configs_restaurant_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.MaxReferrals).HasColumnName("max_referrals");
            entity.Property(e => e.RefereeReward)
                .HasDefaultValueSql("'{\"type\": \"discount_percentage\", \"value\": 10, \"freeItemId\": null}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("referee_reward");
            entity.Property(e => e.ReferrerReward)
                .HasDefaultValueSql("'{\"type\": \"points\", \"value\": 100, \"freeItemId\": null}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("referrer_reward");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithOne(p => p.ReferralConfig)
                .HasForeignKey<ReferralConfig>(d => d.RestaurantId)
                .HasConstraintName("referral_configs_restaurant_id_fkey");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("refresh_tokens_pkey");

            entity.ToTable("refresh_tokens", "auth", tb => tb.HasComment("Auth: Store of tokens used to refresh JWT tokens once they expire."));

            entity.HasIndex(e => e.InstanceId, "refresh_tokens_instance_id_idx");

            entity.HasIndex(e => new { e.InstanceId, e.UserId }, "refresh_tokens_instance_id_user_id_idx");

            entity.HasIndex(e => e.Parent, "refresh_tokens_parent_idx");

            entity.HasIndex(e => new { e.SessionId, e.Revoked }, "refresh_tokens_session_id_revoked_idx");

            entity.HasIndex(e => e.Token, "refresh_tokens_token_unique").IsUnique();

            entity.HasIndex(e => e.UpdatedAt, "refresh_tokens_updated_at_idx").IsDescending();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.InstanceId).HasColumnName("instance_id");
            entity.Property(e => e.Parent)
                .HasMaxLength(255)
                .HasColumnName("parent");
            entity.Property(e => e.Revoked).HasColumnName("revoked");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.Token)
                .HasMaxLength(255)
                .HasColumnName("token");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UserId)
                .HasMaxLength(255)
                .HasColumnName("user_id");

            entity.HasOne(d => d.Session).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.SessionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("refresh_tokens_session_id_fkey");
        });

        modelBuilder.Entity<ReportSchedule>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("report_schedules_pkey");

            entity.ToTable("report_schedules");

            entity.HasIndex(e => e.RestaurantId, "report_schedules_restaurant_id_idx");

            entity.HasIndex(e => e.SavedReportId, "report_schedules_saved_report_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DayOfMonth).HasColumnName("day_of_month");
            entity.Property(e => e.DayOfWeek).HasColumnName("day_of_week");
            entity.Property(e => e.Frequency)
                .HasDefaultValueSql("'daily'::text")
                .HasColumnName("frequency");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LastSentAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_sent_at");
            entity.Property(e => e.RecipientEmails)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("recipient_emails");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.SavedReportId).HasColumnName("saved_report_id");
            entity.Property(e => e.TimeOfDay)
                .HasDefaultValueSql("'08:00'::text")
                .HasColumnName("time_of_day");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.ReportSchedules)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("report_schedules_restaurant_id_fkey");

            entity.HasOne(d => d.SavedReport).WithMany(p => p.ReportSchedules)
                .HasForeignKey(d => d.SavedReportId)
                .HasConstraintName("report_schedules_saved_report_id_fkey");
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("reservations_pkey");

            entity.ToTable("reservations");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ConfirmationSent).HasColumnName("confirmation_sent");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CustomerEmail).HasColumnName("customer_email");
            entity.Property(e => e.CustomerId).HasColumnName("customer_id");
            entity.Property(e => e.CustomerName).HasColumnName("customer_name");
            entity.Property(e => e.CustomerPhone).HasColumnName("customer_phone");
            entity.Property(e => e.PartySize).HasColumnName("party_size");
            entity.Property(e => e.ReminderSent).HasColumnName("reminder_sent");
            entity.Property(e => e.ReservationTime)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("reservation_time");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.SpecialRequests).HasColumnName("special_requests");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'::text")
                .HasColumnName("status");
            entity.Property(e => e.TableNumber).HasColumnName("table_number");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Customer).WithMany(p => p.Reservations)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("reservations_customer_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Reservations)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("reservations_restaurant_id_fkey");
        });

        modelBuilder.Entity<Restaurant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("restaurants_pkey");

            entity.ToTable("restaurants");

            entity.HasIndex(e => e.Slug, "restaurants_slug_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.Address).HasColumnName("address");
            entity.Property(e => e.AiSettings)
                .HasColumnType("jsonb")
                .HasColumnName("ai_settings");
            entity.Property(e => e.BusinessCategory).HasColumnName("business_category");
            entity.Property(e => e.BusinessHours)
                .HasColumnType("jsonb")
                .HasColumnName("business_hours");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CuisineType).HasColumnName("cuisine_type");
            entity.Property(e => e.DefaultBrandingColor).HasColumnName("default_branding_color");
            entity.Property(e => e.DefaultBrandingLogoUrl).HasColumnName("default_branding_logo_url");
            entity.Property(e => e.DefaultInvoiceNotes).HasColumnName("default_invoice_notes");
            entity.Property(e => e.DeliveryEnabled).HasColumnName("delivery_enabled");
            entity.Property(e => e.DeliveryPercentage).HasColumnName("delivery_percentage");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.DineInEnabled).HasColumnName("dine_in_enabled");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.HasUsedTrial).HasColumnName("has_used_trial");
            entity.Property(e => e.Latitude)
                .HasPrecision(10, 7)
                .HasColumnName("latitude");
            entity.Property(e => e.Location).HasColumnName("location");
            entity.Property(e => e.Logo).HasColumnName("logo");
            entity.Property(e => e.Longitude)
                .HasPrecision(10, 7)
                .HasColumnName("longitude");
            entity.Property(e => e.MerchantProfile)
                .HasColumnType("jsonb")
                .HasColumnName("merchant_profile");
            entity.Property(e => e.MonthlyRevenue)
                .HasPrecision(10, 2)
                .HasColumnName("monthly_revenue");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.NotificationSettings)
                .HasColumnType("jsonb")
                .HasColumnName("notification_settings");
            entity.Property(e => e.PaymentProcessor).HasColumnName("payment_processor");
            entity.Property(e => e.PaypalMerchantId).HasColumnName("paypal_merchant_id");
            entity.Property(e => e.Phone)
                .HasDefaultValueSql("''::text")
                .HasColumnName("phone");
            entity.Property(e => e.PickupEnabled).HasColumnName("pickup_enabled");
            entity.Property(e => e.PlanTier)
                .HasDefaultValueSql("'free'::text")
                .HasColumnName("plan_tier");
            entity.Property(e => e.PlatformFeeFixed).HasColumnName("platform_fee_fixed");
            entity.Property(e => e.PlatformFeePercent).HasColumnName("platform_fee_percent");
            entity.Property(e => e.PlatformsUsed)
                .HasDefaultValueSql("ARRAY[]::text[]")
                .HasColumnName("platforms_used");
            entity.Property(e => e.PosSystem)
                .HasDefaultValueSql("'OrderStack'::text")
                .HasColumnName("pos_system");
            entity.Property(e => e.RestaurantGroupId).HasColumnName("restaurant_group_id");
            entity.Property(e => e.Slug).HasColumnName("slug");
            entity.Property(e => e.State).HasColumnName("state");
            entity.Property(e => e.StripeConnectedAccountId).HasColumnName("stripe_connected_account_id");
            entity.Property(e => e.TaxRate)
                .HasPrecision(5, 4)
                .HasColumnName("tax_rate");
            entity.Property(e => e.Tier)
                .HasDefaultValue(1)
                .HasColumnName("tier");
            entity.Property(e => e.TrialEndsAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("trial_ends_at");
            entity.Property(e => e.TrialExpiredAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("trial_expired_at");
            entity.Property(e => e.TrialStartedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("trial_started_at");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Zip).HasColumnName("zip");

            entity.HasOne(d => d.RestaurantGroup).WithMany(p => p.Restaurants)
                .HasForeignKey(d => d.RestaurantGroupId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("restaurants_restaurant_group_id_fkey");
        });

        modelBuilder.Entity<RestaurantAiCredential>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("restaurant_ai_credentials_pkey");

            entity.ToTable("restaurant_ai_credentials");

            entity.HasIndex(e => e.RestaurantId, "restaurant_ai_credentials_restaurant_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EncryptedApiKey).HasColumnName("encrypted_api_key");
            entity.Property(e => e.EncryptionIv).HasColumnName("encryption_iv");
            entity.Property(e => e.EncryptionTag).HasColumnName("encryption_tag");
            entity.Property(e => e.IsValid).HasColumnName("is_valid");
            entity.Property(e => e.KeyLastFour).HasColumnName("key_last_four");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithOne(p => p.RestaurantAiCredential)
                .HasForeignKey<RestaurantAiCredential>(d => d.RestaurantId)
                .HasConstraintName("restaurant_ai_credentials_restaurant_id_fkey");
        });

        modelBuilder.Entity<RestaurantDeliveryCredential>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("restaurant_delivery_credentials_pkey");

            entity.ToTable("restaurant_delivery_credentials");

            entity.HasIndex(e => e.RestaurantId, "restaurant_delivery_credentials_restaurant_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DoordashApiKeyEncrypted).HasColumnName("doordash_api_key_encrypted");
            entity.Property(e => e.DoordashMode).HasColumnName("doordash_mode");
            entity.Property(e => e.DoordashSigningSecretEncrypted).HasColumnName("doordash_signing_secret_encrypted");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UberClientIdEncrypted).HasColumnName("uber_client_id_encrypted");
            entity.Property(e => e.UberClientSecretEncrypted).HasColumnName("uber_client_secret_encrypted");
            entity.Property(e => e.UberCustomerIdEncrypted).HasColumnName("uber_customer_id_encrypted");
            entity.Property(e => e.UberWebhookSigningKeyEncrypted).HasColumnName("uber_webhook_signing_key_encrypted");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithOne(p => p.RestaurantDeliveryCredential)
                .HasForeignKey<RestaurantDeliveryCredential>(d => d.RestaurantId)
                .HasConstraintName("restaurant_delivery_credentials_restaurant_id_fkey");
        });

        modelBuilder.Entity<RestaurantGroup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("restaurant_groups_pkey");

            entity.ToTable("restaurant_groups");

            entity.HasIndex(e => e.Slug, "restaurant_groups_slug_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Logo).HasColumnName("logo");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Slug).HasColumnName("slug");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<RestaurantLoyaltyConfig>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("restaurant_loyalty_config_pkey");

            entity.ToTable("restaurant_loyalty_config");

            entity.HasIndex(e => e.RestaurantId, "restaurant_loyalty_config_restaurant_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.GoldMultiplier)
                .HasPrecision(3, 2)
                .HasDefaultValue(1.50m)
                .HasColumnName("gold_multiplier");
            entity.Property(e => e.PlatinumMultiplier)
                .HasPrecision(3, 2)
                .HasDefaultValue(2.00m)
                .HasColumnName("platinum_multiplier");
            entity.Property(e => e.PointsPerDollar)
                .HasDefaultValue(1)
                .HasColumnName("points_per_dollar");
            entity.Property(e => e.PointsRedemptionRate)
                .HasPrecision(5, 2)
                .HasDefaultValue(0.01m)
                .HasColumnName("points_redemption_rate");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.SilverMultiplier)
                .HasPrecision(3, 2)
                .HasDefaultValue(1.25m)
                .HasColumnName("silver_multiplier");
            entity.Property(e => e.TierGoldMin)
                .HasDefaultValue(2000)
                .HasColumnName("tier_gold_min");
            entity.Property(e => e.TierPlatinumMin)
                .HasDefaultValue(5000)
                .HasColumnName("tier_platinum_min");
            entity.Property(e => e.TierSilverMin)
                .HasDefaultValue(500)
                .HasColumnName("tier_silver_min");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithOne(p => p.RestaurantLoyaltyConfig)
                .HasForeignKey<RestaurantLoyaltyConfig>(d => d.RestaurantId)
                .HasConstraintName("restaurant_loyalty_config_restaurant_id_fkey");
        });

        modelBuilder.Entity<RestaurantProviderProfile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("restaurant_provider_profiles_pkey");

            entity.ToTable("restaurant_provider_profiles");

            entity.HasIndex(e => e.KeyBackend, "restaurant_provider_profiles_key_backend_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.ProfileState }, "restaurant_provider_profiles_restaurant_id_profile_state_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Provider }, "restaurant_provider_profiles_restaurant_id_provider_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AadHash).HasColumnName("aad_hash");
            entity.Property(e => e.ConfigRefMap)
                .HasColumnType("jsonb")
                .HasColumnName("config_ref_map");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DekVersion)
                .HasDefaultValue(1)
                .HasColumnName("dek_version");
            entity.Property(e => e.KeyBackend).HasColumnName("key_backend");
            entity.Property(e => e.KeyRef).HasColumnName("key_ref");
            entity.Property(e => e.ProfileState)
                .HasDefaultValueSql("'ACTIVE'::text")
                .HasColumnName("profile_state");
            entity.Property(e => e.ProfileVersion)
                .HasDefaultValue(1)
                .HasColumnName("profile_version");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.RotatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("rotated_at");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.WrappedDek).HasColumnName("wrapped_dek");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.RestaurantProviderProfiles)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("restaurant_provider_profiles_restaurant_id_fkey");
        });

        modelBuilder.Entity<RestaurantProviderProfileEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("restaurant_provider_profile_events_pkey");

            entity.ToTable("restaurant_provider_profile_events");

            entity.HasIndex(e => e.CorrelationId, "restaurant_provider_profile_events_correlation_id_idx");

            entity.HasIndex(e => new { e.ProfileId, e.CreatedAt }, "restaurant_provider_profile_events_profile_id_created_at_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Provider, e.CreatedAt }, "restaurant_provider_profile_events_restaurant_id_provider_c_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.Actor).HasColumnName("actor");
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.Outcome).HasColumnName("outcome");
            entity.Property(e => e.ProfileId).HasColumnName("profile_id");
            entity.Property(e => e.ProfileVersion).HasColumnName("profile_version");
            entity.Property(e => e.Provider).HasColumnName("provider");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");

            entity.HasOne(d => d.Profile).WithMany(p => p.RestaurantProviderProfileEvents)
                .HasForeignKey(d => d.ProfileId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("restaurant_provider_profile_events_profile_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.RestaurantProviderProfileEvents)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("restaurant_provider_profile_events_restaurant_id_fkey");
        });

        modelBuilder.Entity<RestaurantSupplierCredential>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("restaurant_supplier_credentials_pkey");

            entity.ToTable("restaurant_supplier_credentials");

            entity.HasIndex(e => e.RestaurantId, "restaurant_supplier_credentials_restaurant_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.GfsClientIdEncrypted).HasColumnName("gfs_client_id_encrypted");
            entity.Property(e => e.GfsClientSecretEncrypted).HasColumnName("gfs_client_secret_encrypted");
            entity.Property(e => e.GfsCustomerIdEncrypted).HasColumnName("gfs_customer_id_encrypted");
            entity.Property(e => e.GfsMode).HasColumnName("gfs_mode");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.SyscoClientIdEncrypted).HasColumnName("sysco_client_id_encrypted");
            entity.Property(e => e.SyscoClientSecretEncrypted).HasColumnName("sysco_client_secret_encrypted");
            entity.Property(e => e.SyscoCustomerIdEncrypted).HasColumnName("sysco_customer_id_encrypted");
            entity.Property(e => e.SyscoMode).HasColumnName("sysco_mode");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithOne(p => p.RestaurantSupplierCredential)
                .HasForeignKey<RestaurantSupplierCredential>(d => d.RestaurantId)
                .HasConstraintName("restaurant_supplier_credentials_restaurant_id_fkey");
        });

        modelBuilder.Entity<RestaurantTable>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("restaurant_tables_pkey");

            entity.ToTable("restaurant_tables");

            entity.HasIndex(e => new { e.RestaurantId, e.TableNumber }, "restaurant_tables_restaurant_id_table_number_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.Capacity)
                .HasDefaultValue(4)
                .HasColumnName("capacity");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.PosX).HasColumnName("pos_x");
            entity.Property(e => e.PosY).HasColumnName("pos_y");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Section).HasColumnName("section");
            entity.Property(e => e.ServerName).HasColumnName("server_name");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'available'::text")
                .HasColumnName("status");
            entity.Property(e => e.TableName).HasColumnName("table_name");
            entity.Property(e => e.TableNumber).HasColumnName("table_number");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.RestaurantTables)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("restaurant_tables_restaurant_id_fkey");
        });

        modelBuilder.Entity<RetailCategory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("retail_categories_pkey");

            entity.ToTable("retail_categories");

            entity.HasIndex(e => e.RestaurantId, "retail_categories_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("retail_categories_parent_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.RetailCategories)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("retail_categories_restaurant_id_fkey");
        });

        modelBuilder.Entity<RetailItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("retail_items_pkey");

            entity.ToTable("retail_items");

            entity.HasIndex(e => new { e.RestaurantId, e.Barcode }, "retail_items_restaurant_id_barcode_idx");

            entity.HasIndex(e => e.RestaurantId, "retail_items_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Sku }, "retail_items_restaurant_id_sku_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Barcode).HasColumnName("barcode");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.Cost)
                .HasPrecision(10, 2)
                .HasColumnName("cost");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.ImageUrl).HasColumnName("image_url");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Price)
                .HasPrecision(10, 2)
                .HasColumnName("price");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Sku).HasColumnName("sku");
            entity.Property(e => e.TrackStock)
                .HasDefaultValue(true)
                .HasColumnName("track_stock");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Category).WithMany(p => p.RetailItems)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("retail_items_category_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.RetailItems)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("retail_items_restaurant_id_fkey");
        });

        modelBuilder.Entity<RetailItemOptionSet>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("retail_item_option_sets_pkey");

            entity.ToTable("retail_item_option_sets");

            entity.HasIndex(e => new { e.RetailItemId, e.OptionSetId }, "retail_item_option_sets_retail_item_id_option_set_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OptionSetId).HasColumnName("option_set_id");
            entity.Property(e => e.RetailItemId).HasColumnName("retail_item_id");

            entity.HasOne(d => d.OptionSet).WithMany(p => p.RetailItemOptionSets)
                .HasForeignKey(d => d.OptionSetId)
                .HasConstraintName("retail_item_option_sets_option_set_id_fkey");

            entity.HasOne(d => d.RetailItem).WithMany(p => p.RetailItemOptionSets)
                .HasForeignKey(d => d.RetailItemId)
                .HasConstraintName("retail_item_option_sets_retail_item_id_fkey");
        });

        modelBuilder.Entity<RetailOption>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("retail_options_pkey");

            entity.ToTable("retail_options");

            entity.HasIndex(e => e.OptionSetId, "retail_options_option_set_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OptionSetId).HasColumnName("option_set_id");
            entity.Property(e => e.PriceAdjustment)
                .HasPrecision(10, 2)
                .HasColumnName("price_adjustment");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");

            entity.HasOne(d => d.OptionSet).WithMany(p => p.RetailOptions)
                .HasForeignKey(d => d.OptionSetId)
                .HasConstraintName("retail_options_option_set_id_fkey");
        });

        modelBuilder.Entity<RetailOptionSet>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("retail_option_sets_pkey");

            entity.ToTable("retail_option_sets");

            entity.HasIndex(e => e.RestaurantId, "retail_option_sets_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.RetailOptionSets)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("retail_option_sets_restaurant_id_fkey");
        });

        modelBuilder.Entity<RetailQuickKey>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("retail_quick_keys_pkey");

            entity.ToTable("retail_quick_keys");

            entity.HasIndex(e => e.RestaurantId, "retail_quick_keys_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Color).HasColumnName("color");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Label).HasColumnName("label");
            entity.Property(e => e.Position).HasColumnName("position");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.RetailItemId).HasColumnName("retail_item_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.RetailQuickKeys)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("retail_quick_keys_restaurant_id_fkey");
        });

        modelBuilder.Entity<RetailStock>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("retail_stock_pkey");

            entity.ToTable("retail_stock");

            entity.HasIndex(e => e.RestaurantId, "retail_stock_restaurant_id_idx");

            entity.HasIndex(e => e.RetailItemId, "retail_stock_retail_item_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Location).HasColumnName("location");
            entity.Property(e => e.LowStockThreshold)
                .HasDefaultValue(5)
                .HasColumnName("low_stock_threshold");
            entity.Property(e => e.Quantity).HasColumnName("quantity");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.RetailItemId).HasColumnName("retail_item_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.RetailStocks)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("retail_stock_restaurant_id_fkey");

            entity.HasOne(d => d.RetailItem).WithOne(p => p.RetailStock)
                .HasForeignKey<RetailStock>(d => d.RetailItemId)
                .HasConstraintName("retail_stock_retail_item_id_fkey");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Role_pkey");

            entity.ToTable("Role", "geek_auth");

            entity.HasIndex(e => e.Name, "Role_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name).HasColumnName("name");

            entity.HasMany(d => d.Permissions).WithMany(p => p.Roles)
                .UsingEntity<Dictionary<string, object>>(
                    "RolePermission",
                    r => r.HasOne<Permission>().WithMany()
                        .HasForeignKey("PermissionId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .HasConstraintName("RolePermission_permissionId_fkey"),
                    l => l.HasOne<Role>().WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .HasConstraintName("RolePermission_roleId_fkey"),
                    j =>
                    {
                        j.HasKey("RoleId", "PermissionId").HasName("RolePermission_pkey");
                        j.ToTable("RolePermission", "geek_auth");
                        j.IndexerProperty<string>("RoleId").HasColumnName("roleId");
                        j.IndexerProperty<string>("PermissionId").HasColumnName("permissionId");
                    });
        });

        modelBuilder.Entity<S3MultipartUpload>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("s3_multipart_uploads_pkey");

            entity.ToTable("s3_multipart_uploads", "storage");

            entity.HasIndex(e => new { e.BucketId, e.Key, e.CreatedAt }, "idx_multipart_uploads_list").UseCollation(new[] { null, "C", null });

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BucketId).HasColumnName("bucket_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.InProgressSize).HasColumnName("in_progress_size");
            entity.Property(e => e.Key)
                .UseCollation("C")
                .HasColumnName("key");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasColumnName("metadata");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.UploadSignature).HasColumnName("upload_signature");
            entity.Property(e => e.UserMetadata)
                .HasColumnType("jsonb")
                .HasColumnName("user_metadata");
            entity.Property(e => e.Version).HasColumnName("version");

            entity.HasOne(d => d.Bucket).WithMany(p => p.S3MultipartUploads)
                .HasForeignKey(d => d.BucketId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("s3_multipart_uploads_bucket_id_fkey");
        });

        modelBuilder.Entity<S3MultipartUploadsPart>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("s3_multipart_uploads_parts_pkey");

            entity.ToTable("s3_multipart_uploads_parts", "storage");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.BucketId).HasColumnName("bucket_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Etag).HasColumnName("etag");
            entity.Property(e => e.Key)
                .UseCollation("C")
                .HasColumnName("key");
            entity.Property(e => e.OwnerId).HasColumnName("owner_id");
            entity.Property(e => e.PartNumber).HasColumnName("part_number");
            entity.Property(e => e.Size).HasColumnName("size");
            entity.Property(e => e.UploadId).HasColumnName("upload_id");
            entity.Property(e => e.Version).HasColumnName("version");

            entity.HasOne(d => d.Bucket).WithMany(p => p.S3MultipartUploadsParts)
                .HasForeignKey(d => d.BucketId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("s3_multipart_uploads_parts_bucket_id_fkey");

            entity.HasOne(d => d.Upload).WithMany(p => p.S3MultipartUploadsParts)
                .HasForeignKey(d => d.UploadId)
                .HasConstraintName("s3_multipart_uploads_parts_upload_id_fkey");
        });

        modelBuilder.Entity<SamlProvider>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("saml_providers_pkey");

            entity.ToTable("saml_providers", "auth", tb => tb.HasComment("Auth: Manages SAML Identity Provider connections."));

            entity.HasIndex(e => e.EntityId, "saml_providers_entity_id_key").IsUnique();

            entity.HasIndex(e => e.SsoProviderId, "saml_providers_sso_provider_id_idx");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AttributeMapping)
                .HasColumnType("jsonb")
                .HasColumnName("attribute_mapping");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.MetadataUrl).HasColumnName("metadata_url");
            entity.Property(e => e.MetadataXml).HasColumnName("metadata_xml");
            entity.Property(e => e.NameIdFormat).HasColumnName("name_id_format");
            entity.Property(e => e.SsoProviderId).HasColumnName("sso_provider_id");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.SsoProvider).WithMany(p => p.SamlProviders)
                .HasForeignKey(d => d.SsoProviderId)
                .HasConstraintName("saml_providers_sso_provider_id_fkey");
        });

        modelBuilder.Entity<SamlRelayState>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("saml_relay_states_pkey");

            entity.ToTable("saml_relay_states", "auth", tb => tb.HasComment("Auth: Contains SAML Relay State information for each Service Provider initiated login."));

            entity.HasIndex(e => e.CreatedAt, "saml_relay_states_created_at_idx").IsDescending();

            entity.HasIndex(e => e.ForEmail, "saml_relay_states_for_email_idx");

            entity.HasIndex(e => e.SsoProviderId, "saml_relay_states_sso_provider_id_idx");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.FlowStateId).HasColumnName("flow_state_id");
            entity.Property(e => e.ForEmail).HasColumnName("for_email");
            entity.Property(e => e.RedirectTo).HasColumnName("redirect_to");
            entity.Property(e => e.RequestId).HasColumnName("request_id");
            entity.Property(e => e.SsoProviderId).HasColumnName("sso_provider_id");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.FlowState).WithMany(p => p.SamlRelayStates)
                .HasForeignKey(d => d.FlowStateId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("saml_relay_states_flow_state_id_fkey");

            entity.HasOne(d => d.SsoProvider).WithMany(p => p.SamlRelayStates)
                .HasForeignKey(d => d.SsoProviderId)
                .HasConstraintName("saml_relay_states_sso_provider_id_fkey");
        });

        modelBuilder.Entity<SavedReport>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("saved_reports_pkey");

            entity.ToTable("saved_reports");

            entity.HasIndex(e => e.RestaurantId, "saved_reports_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Blocks)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("blocks");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy)
                .HasDefaultValueSql("'system'::text")
                .HasColumnName("created_by");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.SavedReports)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("saved_reports_restaurant_id_fkey");
        });

        modelBuilder.Entity<ScheduleTemplate>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("schedule_templates_pkey");

            entity.ToTable("schedule_templates");

            entity.HasIndex(e => e.RestaurantId, "schedule_templates_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<SchemaMigration>(entity =>
        {
            entity.HasKey(e => e.Version).HasName("schema_migrations_pkey");

            entity.ToTable("schema_migrations", "auth", tb => tb.HasComment("Auth: Manages updates to the auth system."));

            entity.Property(e => e.Version)
                .HasMaxLength(255)
                .HasColumnName("version");
        });

        modelBuilder.Entity<SchemaMigration1>(entity =>
        {
            entity.HasKey(e => e.Version).HasName("schema_migrations_pkey");

            entity.ToTable("schema_migrations", "realtime");

            entity.Property(e => e.Version)
                .ValueGeneratedNever()
                .HasColumnName("version");
            entity.Property(e => e.InsertedAt)
                .HasColumnType("timestamp(0) without time zone")
                .HasColumnName("inserted_at");
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("sessions_pkey");

            entity.ToTable("sessions", "auth", tb => tb.HasComment("Auth: Stores session data associated to a user."));

            entity.HasIndex(e => e.NotAfter, "sessions_not_after_idx").IsDescending();

            entity.HasIndex(e => e.OauthClientId, "sessions_oauth_client_id_idx");

            entity.HasIndex(e => e.UserId, "sessions_user_id_idx");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }, "user_id_created_at_idx");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.FactorId).HasColumnName("factor_id");
            entity.Property(e => e.Ip).HasColumnName("ip");
            entity.Property(e => e.NotAfter)
                .HasComment("Auth: Not after is a nullable column that contains a timestamp after which the session should be regarded as expired.")
                .HasColumnName("not_after");
            entity.Property(e => e.OauthClientId).HasColumnName("oauth_client_id");
            entity.Property(e => e.RefreshTokenCounter)
                .HasComment("Holds the ID (counter) of the last issued refresh token.")
                .HasColumnName("refresh_token_counter");
            entity.Property(e => e.RefreshTokenHmacKey)
                .HasComment("Holds a HMAC-SHA256 key used to sign refresh tokens for this session.")
                .HasColumnName("refresh_token_hmac_key");
            entity.Property(e => e.RefreshedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("refreshed_at");
            entity.Property(e => e.Scopes).HasColumnName("scopes");
            entity.Property(e => e.Tag).HasColumnName("tag");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UserAgent).HasColumnName("user_agent");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.OauthClient).WithMany(p => p.Sessions)
                .HasForeignKey(d => d.OauthClientId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("sessions_oauth_client_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Sessions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("sessions_user_id_fkey");
        });

        modelBuilder.Entity<Session1>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Session_pkey");

            entity.ToTable("Session", "seo");

            entity.HasIndex(e => e.SessionToken, "Session_sessionToken_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Expires)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expires");
            entity.Property(e => e.SessionToken).HasColumnName("sessionToken");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.Session1s)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("Session_userId_fkey");
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("shifts_pkey");

            entity.ToTable("shifts");

            entity.HasIndex(e => new { e.RestaurantId, e.Date }, "shifts_restaurant_id_date_idx");

            entity.HasIndex(e => e.StaffPinId, "shifts_staff_pin_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BreakMinutes).HasColumnName("break_minutes");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Date).HasColumnName("date");
            entity.Property(e => e.EndTime)
                .HasMaxLength(5)
                .HasColumnName("endTime");
            entity.Property(e => e.IsPublished).HasColumnName("is_published");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.Position)
                .HasDefaultValueSql("'server'::text")
                .HasColumnName("position");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.StaffPinId).HasColumnName("staff_pin_id");
            entity.Property(e => e.StartTime)
                .HasMaxLength(5)
                .HasColumnName("startTime");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.StaffPin).WithMany(p => p.Shifts)
                .HasForeignKey(d => d.StaffPinId)
                .HasConstraintName("shifts_staff_pin_id_fkey");
        });

        modelBuilder.Entity<Site>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Site_pkey");

            entity.ToTable("Site", "seo");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AutoPublish).HasColumnName("autoPublish");
            entity.Property(e => e.CmsApiKey).HasColumnName("cmsApiKey");
            entity.Property(e => e.CmsApiUrl).HasColumnName("cmsApiUrl");
            entity.Property(e => e.CmsSiteId).HasColumnName("cmsSiteId");
            entity.Property(e => e.CmsType)
                .HasDefaultValueSql("'wordpress'::text")
                .HasColumnName("cmsType");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Domain).HasColumnName("domain");
            entity.Property(e => e.Language)
                .HasDefaultValueSql("'en'::text")
                .HasColumnName("language");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.PostFrequency)
                .HasDefaultValueSql("'weekly'::text")
                .HasColumnName("postFrequency");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updatedAt");
            entity.Property(e => e.UserId).HasColumnName("userId");

            entity.HasOne(d => d.User).WithMany(p => p.Sites)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("Site_userId_fkey");
        });

        modelBuilder.Entity<SmartGroup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("smart_groups_pkey");

            entity.ToTable("smart_groups");

            entity.HasIndex(e => e.RestaurantId, "smart_groups_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Rules)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("rules");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.SmartGroups)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("smart_groups_restaurant_id_fkey");
        });

        modelBuilder.Entity<SsoDomain>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("sso_domains_pkey");

            entity.ToTable("sso_domains", "auth", tb => tb.HasComment("Auth: Manages SSO email address domain mapping to an SSO Identity Provider."));

            entity.HasIndex(e => e.SsoProviderId, "sso_domains_sso_provider_id_idx");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.Domain).HasColumnName("domain");
            entity.Property(e => e.SsoProviderId).HasColumnName("sso_provider_id");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.SsoProvider).WithMany(p => p.SsoDomains)
                .HasForeignKey(d => d.SsoProviderId)
                .HasConstraintName("sso_domains_sso_provider_id_fkey");
        });

        modelBuilder.Entity<SsoProvider>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("sso_providers_pkey");

            entity.ToTable("sso_providers", "auth", tb => tb.HasComment("Auth: Manages SSO identity provider information; see saml_providers for SAML."));

            entity.HasIndex(e => e.ResourceId, "sso_providers_resource_id_pattern_idx").HasOperators(new[] { "text_pattern_ops" });

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.Disabled).HasColumnName("disabled");
            entity.Property(e => e.ResourceId)
                .HasComment("Auth: Uniquely identifies a SSO provider according to a user-chosen resource ID (case insensitive), useful in infrastructure as code.")
                .HasColumnName("resource_id");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<StaffAvailability>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("staff_availability_pkey");

            entity.ToTable("staff_availability");

            entity.HasIndex(e => e.RestaurantId, "staff_availability_restaurant_id_idx");

            entity.HasIndex(e => new { e.StaffPinId, e.DayOfWeek }, "staff_availability_staff_pin_id_day_of_week_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DayOfWeek).HasColumnName("day_of_week");
            entity.Property(e => e.IsAvailable)
                .HasDefaultValue(true)
                .HasColumnName("is_available");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.PreferredEnd)
                .HasMaxLength(5)
                .HasColumnName("preferred_end");
            entity.Property(e => e.PreferredStart)
                .HasMaxLength(5)
                .HasColumnName("preferred_start");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.StaffPinId).HasColumnName("staff_pin_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.StaffPin).WithMany(p => p.StaffAvailabilities)
                .HasForeignKey(d => d.StaffPinId)
                .HasConstraintName("staff_availability_staff_pin_id_fkey");
        });

        modelBuilder.Entity<StaffNotification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("staff_notifications_pkey");

            entity.ToTable("staff_notifications");

            entity.HasIndex(e => e.RecipientPinId, "staff_notifications_recipient_pin_id_idx");

            entity.HasIndex(e => new { e.RecipientPinId, e.IsRead }, "staff_notifications_recipient_pin_id_is_read_idx");

            entity.HasIndex(e => e.RestaurantId, "staff_notifications_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsRead).HasColumnName("is_read");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.RecipientPinId).HasColumnName("recipient_pin_id");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Type).HasColumnName("type");
        });

        modelBuilder.Entity<StaffPin>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("staff_pins_pkey");

            entity.ToTable("staff_pins");

            entity.HasIndex(e => e.RestaurantId, "staff_pins_restaurant_id_idx");

            entity.HasIndex(e => e.TeamMemberId, "staff_pins_team_member_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Pin).HasColumnName("pin");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Role)
                .HasDefaultValueSql("'staff'::text")
                .HasColumnName("role");
            entity.Property(e => e.TeamMemberId).HasColumnName("team_member_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.TeamMember).WithOne(p => p.StaffPin)
                .HasForeignKey<StaffPin>(d => d.TeamMemberId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("staff_pins_team_member_id_fkey");
        });

        modelBuilder.Entity<StaffTaxInfo>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("staff_tax_info_pkey");

            entity.ToTable("staff_tax_info");

            entity.HasIndex(e => e.TeamMemberId, "staff_tax_info_team_member_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Deductions).HasColumnName("deductions");
            entity.Property(e => e.ExtraWithholding).HasColumnName("extra_withholding");
            entity.Property(e => e.FilingStatus)
                .HasDefaultValueSql("'single'::text")
                .HasColumnName("filing_status");
            entity.Property(e => e.MultipleJobs).HasColumnName("multiple_jobs");
            entity.Property(e => e.OtherDependentsAmount).HasColumnName("other_dependents_amount");
            entity.Property(e => e.OtherIncome).HasColumnName("other_income");
            entity.Property(e => e.QualifyingChildrenAmount).HasColumnName("qualifying_children_amount");
            entity.Property(e => e.State)
                .HasDefaultValueSql("'FL'::text")
                .HasColumnName("state");
            entity.Property(e => e.TeamMemberId).HasColumnName("team_member_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.TeamMember).WithOne(p => p.StaffTaxInfo)
                .HasForeignKey<StaffTaxInfo>(d => d.TeamMemberId)
                .HasConstraintName("staff_tax_info_team_member_id_fkey");
        });

        modelBuilder.Entity<Station>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("stations_pkey");

            entity.ToTable("stations");

            entity.HasIndex(e => e.RestaurantId, "stations_restaurant_id_idx");

            entity.HasIndex(e => new { e.RestaurantId, e.Name }, "stations_restaurant_id_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Color).HasColumnName("color");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DisplayOrder).HasColumnName("display_order");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsExpo).HasColumnName("is_expo");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<StationCategoryMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("station_category_mappings_pkey");

            entity.ToTable("station_category_mappings");

            entity.HasIndex(e => e.CategoryId, "station_category_mappings_category_id_idx");

            entity.HasIndex(e => e.RestaurantId, "station_category_mappings_restaurant_id_idx");

            entity.HasIndex(e => new { e.StationId, e.CategoryId }, "station_category_mappings_station_id_category_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.StationId).HasColumnName("station_id");

            entity.HasOne(d => d.Category).WithMany(p => p.StationCategoryMappings)
                .HasForeignKey(d => d.CategoryId)
                .HasConstraintName("station_category_mappings_category_id_fkey");

            entity.HasOne(d => d.Station).WithMany(p => p.StationCategoryMappings)
                .HasForeignKey(d => d.StationId)
                .HasConstraintName("station_category_mappings_station_id_fkey");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("subscriptions_pkey");

            entity.ToTable("subscriptions");

            entity.HasIndex(e => e.RestaurantId, "subscriptions_restaurant_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CancelAtPeriodEnd).HasColumnName("cancel_at_period_end");
            entity.Property(e => e.CanceledAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("canceled_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CurrentPeriodEnd)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("current_period_end");
            entity.Property(e => e.CurrentPeriodStart)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("current_period_start");
            entity.Property(e => e.PaypalSubscriptionId).HasColumnName("paypal_subscription_id");
            entity.Property(e => e.PlanPrice)
                .HasDefaultValue(5000)
                .HasColumnName("plan_price");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'trialing'::text")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithOne(p => p.Subscription)
                .HasForeignKey<Subscription>(d => d.RestaurantId)
                .HasConstraintName("subscriptions_restaurant_id_fkey");
        });

        modelBuilder.Entity<Subscription1>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_subscription");

            entity.ToTable("subscription", "realtime");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.ActionFilter)
                .HasDefaultValueSql("'*'::text")
                .HasColumnName("action_filter");
            entity.Property(e => e.Claims)
                .HasColumnType("jsonb")
                .HasColumnName("claims");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("timezone('utc'::text, now())")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.SubscriptionId).HasColumnName("subscription_id");
        });

        modelBuilder.Entity<SwapRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("swap_requests_pkey");

            entity.ToTable("swap_requests");

            entity.HasIndex(e => e.RequestorPinId, "swap_requests_requestor_pin_id_idx");

            entity.HasIndex(e => e.RestaurantId, "swap_requests_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.RequestorPinId).HasColumnName("requestor_pin_id");
            entity.Property(e => e.RespondedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("responded_at");
            entity.Property(e => e.RespondedBy).HasColumnName("responded_by");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.ShiftId).HasColumnName("shift_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'::text")
                .HasColumnName("status");
            entity.Property(e => e.TargetPinId).HasColumnName("target_pin_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.RequestorPin).WithMany(p => p.SwapRequests)
                .HasForeignKey(d => d.RequestorPinId)
                .HasConstraintName("swap_requests_requestor_pin_id_fkey");

            entity.HasOne(d => d.Shift).WithMany(p => p.SwapRequests)
                .HasForeignKey(d => d.ShiftId)
                .HasConstraintName("swap_requests_shift_id_fkey");
        });

        modelBuilder.Entity<TaxJurisdiction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("tax_jurisdictions_pkey");

            entity.ToTable("tax_jurisdictions");

            entity.HasIndex(e => new { e.ZipCode, e.State }, "tax_jurisdictions_zip_code_state_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Breakdown)
                .HasColumnType("jsonb")
                .HasColumnName("breakdown");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.County).HasColumnName("county");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Source)
                .HasDefaultValueSql("'ai'::text")
                .HasColumnName("source");
            entity.Property(e => e.State)
                .HasDefaultValueSql("'FL'::text")
                .HasColumnName("state");
            entity.Property(e => e.TaxRate)
                .HasPrecision(5, 4)
                .HasColumnName("tax_rate");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.VerifiedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("verified_at");
            entity.Property(e => e.ZipCode).HasColumnName("zip_code");
        });

        modelBuilder.Entity<TeamMember>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("team_members_pkey");

            entity.ToTable("team_members");

            entity.HasIndex(e => e.Email, "team_members_email_key").IsUnique();

            entity.HasIndex(e => e.RestaurantGroupId, "team_members_restaurant_group_id_idx");

            entity.HasIndex(e => e.RestaurantId, "team_members_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AssignedLocationIds)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("assigned_location_ids");
            entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DisplayName).HasColumnName("display_name");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.FirstName).HasColumnName("first_name");
            entity.Property(e => e.HireDate)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("hire_date");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LastLoginAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_login_at");
            entity.Property(e => e.LastName).HasColumnName("last_name");
            entity.Property(e => e.MustChangePassword).HasColumnName("must_change_password");
            entity.Property(e => e.Passcode).HasColumnName("passcode");
            entity.Property(e => e.PasswordChangedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("password_changed_at");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.PermissionSetId).HasColumnName("permission_set_id");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.RestaurantGroupId).HasColumnName("restaurant_group_id");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Role)
                .HasDefaultValueSql("'staff'::text")
                .HasColumnName("role");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'active'::text")
                .HasColumnName("status");
            entity.Property(e => e.TempPasswordExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("temp_password_expires_at");
            entity.Property(e => e.TempPasswordSetBy).HasColumnName("temp_password_set_by");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.WorkFromHome).HasColumnName("work_from_home");

            entity.HasOne(d => d.PermissionSet).WithMany(p => p.TeamMembers)
                .HasForeignKey(d => d.PermissionSetId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("team_members_permission_set_id_fkey");

            entity.HasOne(d => d.RestaurantGroup).WithMany(p => p.TeamMembers)
                .HasForeignKey(d => d.RestaurantGroupId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("team_members_restaurant_group_id_fkey");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.TeamMembers)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("team_members_restaurant_id_fkey");
        });

        modelBuilder.Entity<TeamMemberJob>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("team_member_jobs_pkey");

            entity.ToTable("team_member_jobs");

            entity.HasIndex(e => e.TeamMemberId, "team_member_jobs_team_member_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.HourlyRate).HasColumnName("hourly_rate");
            entity.Property(e => e.IsPrimary).HasColumnName("is_primary");
            entity.Property(e => e.IsTipEligible).HasColumnName("is_tip_eligible");
            entity.Property(e => e.JobTitle).HasColumnName("job_title");
            entity.Property(e => e.OvertimeEligible)
                .HasDefaultValue(true)
                .HasColumnName("overtime_eligible");
            entity.Property(e => e.TeamMemberId).HasColumnName("team_member_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.TeamMember).WithMany(p => p.TeamMemberJobs)
                .HasForeignKey(d => d.TeamMemberId)
                .HasConstraintName("team_member_jobs_team_member_id_fkey");
        });

        modelBuilder.Entity<TemplateShift>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("template_shifts_pkey");

            entity.ToTable("template_shifts");

            entity.HasIndex(e => e.TemplateId, "template_shifts_template_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BreakMinutes).HasColumnName("break_minutes");
            entity.Property(e => e.DayOfWeek).HasColumnName("day_of_week");
            entity.Property(e => e.EndTime)
                .HasMaxLength(5)
                .HasColumnName("end_time");
            entity.Property(e => e.Position)
                .HasDefaultValueSql("'server'::text")
                .HasColumnName("position");
            entity.Property(e => e.StaffName).HasColumnName("staff_name");
            entity.Property(e => e.StaffPinId).HasColumnName("staff_pin_id");
            entity.Property(e => e.StartTime)
                .HasMaxLength(5)
                .HasColumnName("start_time");
            entity.Property(e => e.TemplateId).HasColumnName("template_id");

            entity.HasOne(d => d.Template).WithMany(p => p.TemplateShifts)
                .HasForeignKey(d => d.TemplateId)
                .HasConstraintName("template_shifts_template_id_fkey");
        });

        modelBuilder.Entity<TimeEntry>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("time_entries_pkey");

            entity.ToTable("time_entries");

            entity.HasIndex(e => new { e.RestaurantId, e.ClockIn }, "time_entries_restaurant_id_clock_in_idx");

            entity.HasIndex(e => e.StaffPinId, "time_entries_staff_pin_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.BreakMinutes).HasColumnName("break_minutes");
            entity.Property(e => e.ClockIn)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("clock_in");
            entity.Property(e => e.ClockOut)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("clock_out");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.ShiftId).HasColumnName("shift_id");
            entity.Property(e => e.StaffPinId).HasColumnName("staff_pin_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.StaffPin).WithMany(p => p.TimeEntries)
                .HasForeignKey(d => d.StaffPinId)
                .HasConstraintName("time_entries_staff_pin_id_fkey");
        });

        modelBuilder.Entity<TimecardEditRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("timecard_edit_requests_pkey");

            entity.ToTable("timecard_edit_requests");

            entity.HasIndex(e => new { e.RestaurantId, e.Status }, "timecard_edit_requests_restaurant_id_status_idx");

            entity.HasIndex(e => e.StaffPinId, "timecard_edit_requests_staff_pin_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EditType).HasColumnName("edit_type");
            entity.Property(e => e.NewValue).HasColumnName("new_value");
            entity.Property(e => e.OriginalValue).HasColumnName("original_value");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.RespondedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("responded_at");
            entity.Property(e => e.RespondedBy).HasColumnName("responded_by");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.StaffPinId).HasColumnName("staff_pin_id");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'::text")
                .HasColumnName("status");
            entity.Property(e => e.TimeEntryId).HasColumnName("time_entry_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.StaffPin).WithMany(p => p.TimecardEditRequests)
                .HasForeignKey(d => d.StaffPinId)
                .HasConstraintName("timecard_edit_requests_staff_pin_id_fkey");

            entity.HasOne(d => d.TimeEntry).WithMany(p => p.TimecardEditRequests)
                .HasForeignKey(d => d.TimeEntryId)
                .HasConstraintName("timecard_edit_requests_time_entry_id_fkey");
        });

        modelBuilder.Entity<UnitConversion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("unit_conversions_pkey");

            entity.ToTable("unit_conversions");

            entity.HasIndex(e => new { e.RestaurantId, e.FromUnit, e.ToUnit }, "unit_conversions_restaurant_id_from_unit_to_unit_key").IsUnique();

            entity.HasIndex(e => e.RestaurantId, "unit_conversions_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Factor)
                .HasPrecision(10, 4)
                .HasColumnName("factor");
            entity.Property(e => e.FromUnit).HasColumnName("from_unit");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.ToUnit).HasColumnName("to_unit");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.UnitConversions)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("unit_conversions_restaurant_id_fkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users", "auth", tb => tb.HasComment("Auth: Stores user login data within a secure schema."));

            entity.HasIndex(e => e.ConfirmationToken, "confirmation_token_idx")
                .IsUnique()
                .HasFilter("((confirmation_token)::text !~ '^[0-9 ]*$'::text)");

            entity.HasIndex(e => e.EmailChangeTokenCurrent, "email_change_token_current_idx")
                .IsUnique()
                .HasFilter("((email_change_token_current)::text !~ '^[0-9 ]*$'::text)");

            entity.HasIndex(e => e.EmailChangeTokenNew, "email_change_token_new_idx")
                .IsUnique()
                .HasFilter("((email_change_token_new)::text !~ '^[0-9 ]*$'::text)");

            entity.HasIndex(e => e.ReauthenticationToken, "reauthentication_token_idx")
                .IsUnique()
                .HasFilter("((reauthentication_token)::text !~ '^[0-9 ]*$'::text)");

            entity.HasIndex(e => e.RecoveryToken, "recovery_token_idx")
                .IsUnique()
                .HasFilter("((recovery_token)::text !~ '^[0-9 ]*$'::text)");

            entity.HasIndex(e => e.Email, "users_email_partial_key")
                .IsUnique()
                .HasFilter("(is_sso_user = false)");

            entity.HasIndex(e => e.InstanceId, "users_instance_id_idx");

            entity.HasIndex(e => e.IsAnonymous, "users_is_anonymous_idx");

            entity.HasIndex(e => e.Phone, "users_phone_key").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Aud)
                .HasMaxLength(255)
                .HasColumnName("aud");
            entity.Property(e => e.BannedUntil).HasColumnName("banned_until");
            entity.Property(e => e.ConfirmationSentAt).HasColumnName("confirmation_sent_at");
            entity.Property(e => e.ConfirmationToken)
                .HasMaxLength(255)
                .HasColumnName("confirmation_token");
            entity.Property(e => e.ConfirmedAt)
                .HasComputedColumnSql("LEAST(email_confirmed_at, phone_confirmed_at)", true)
                .HasColumnName("confirmed_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.EmailChange)
                .HasMaxLength(255)
                .HasColumnName("email_change");
            entity.Property(e => e.EmailChangeConfirmStatus)
                .HasDefaultValue((short)0)
                .HasColumnName("email_change_confirm_status");
            entity.Property(e => e.EmailChangeSentAt).HasColumnName("email_change_sent_at");
            entity.Property(e => e.EmailChangeTokenCurrent)
                .HasMaxLength(255)
                .HasDefaultValueSql("''::character varying")
                .HasColumnName("email_change_token_current");
            entity.Property(e => e.EmailChangeTokenNew)
                .HasMaxLength(255)
                .HasColumnName("email_change_token_new");
            entity.Property(e => e.EmailConfirmedAt).HasColumnName("email_confirmed_at");
            entity.Property(e => e.EncryptedPassword)
                .HasMaxLength(255)
                .HasColumnName("encrypted_password");
            entity.Property(e => e.InstanceId).HasColumnName("instance_id");
            entity.Property(e => e.InvitedAt).HasColumnName("invited_at");
            entity.Property(e => e.IsAnonymous).HasColumnName("is_anonymous");
            entity.Property(e => e.IsSsoUser)
                .HasComment("Auth: Set this column to true when the account comes from SSO. These accounts can have duplicate emails.")
                .HasColumnName("is_sso_user");
            entity.Property(e => e.IsSuperAdmin).HasColumnName("is_super_admin");
            entity.Property(e => e.LastSignInAt).HasColumnName("last_sign_in_at");
            entity.Property(e => e.Phone)
                .HasDefaultValueSql("NULL::character varying")
                .HasColumnName("phone");
            entity.Property(e => e.PhoneChange)
                .HasDefaultValueSql("''::character varying")
                .HasColumnName("phone_change");
            entity.Property(e => e.PhoneChangeSentAt).HasColumnName("phone_change_sent_at");
            entity.Property(e => e.PhoneChangeToken)
                .HasMaxLength(255)
                .HasDefaultValueSql("''::character varying")
                .HasColumnName("phone_change_token");
            entity.Property(e => e.PhoneConfirmedAt).HasColumnName("phone_confirmed_at");
            entity.Property(e => e.RawAppMetaData)
                .HasColumnType("jsonb")
                .HasColumnName("raw_app_meta_data");
            entity.Property(e => e.RawUserMetaData)
                .HasColumnType("jsonb")
                .HasColumnName("raw_user_meta_data");
            entity.Property(e => e.ReauthenticationSentAt).HasColumnName("reauthentication_sent_at");
            entity.Property(e => e.ReauthenticationToken)
                .HasMaxLength(255)
                .HasDefaultValueSql("''::character varying")
                .HasColumnName("reauthentication_token");
            entity.Property(e => e.RecoverySentAt).HasColumnName("recovery_sent_at");
            entity.Property(e => e.RecoveryToken)
                .HasMaxLength(255)
                .HasColumnName("recovery_token");
            entity.Property(e => e.Role)
                .HasMaxLength(255)
                .HasColumnName("role");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<User1>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("User_pkey");

            entity.ToTable("User", "geek_auth");

            entity.HasIndex(e => e.Email, "User_email_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Password).HasColumnName("password");
            entity.Property(e => e.Plan)
                .HasDefaultValueSql("'free'::text")
                .HasColumnName("plan");
            entity.Property(e => e.SlackUserId).HasColumnName("slackUserId");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updatedAt");
        });

        modelBuilder.Entity<User2>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("User_pkey");

            entity.ToTable("User", "seo");

            entity.HasIndex(e => e.Email, "User_email_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Image).HasColumnName("image");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Plan)
                .HasDefaultValueSql("'free'::text")
                .HasColumnName("plan");
        });

        modelBuilder.Entity<UserGoogleCredential>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("user_google_credentials_pkey");

            entity.ToTable("user_google_credentials");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.GoogleRefreshToken).HasColumnName("google_refresh_token");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<UserRestaurantAccess>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_restaurant_access_pkey");

            entity.ToTable("user_restaurant_access");

            entity.HasIndex(e => e.RestaurantId, "user_restaurant_access_restaurant_id_idx");

            entity.HasIndex(e => new { e.UserId, e.RestaurantId }, "user_restaurant_access_user_id_restaurant_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.Role)
                .HasDefaultValueSql("'staff'::text")
                .HasColumnName("role");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.UserRestaurantAccesses)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("user_restaurant_access_restaurant_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.UserRestaurantAccesses)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_restaurant_access_user_id_fkey");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.RoleId }).HasName("UserRole_pkey");

            entity.ToTable("UserRole", "geek_auth");

            entity.Property(e => e.UserId).HasColumnName("userId");
            entity.Property(e => e.RoleId).HasColumnName("roleId");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("createdAt");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("UserRole_roleId_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("UserRole_userId_fkey");
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_sessions_pkey");

            entity.ToTable("user_sessions");

            entity.HasIndex(e => e.Token, "user_sessions_token_key").IsUnique();

            entity.HasIndex(e => e.UserId, "user_sessions_user_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeviceInfo).HasColumnName("device_info");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LastActivityAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("last_activity_at");
            entity.Property(e => e.Token).HasColumnName("token");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.UserSessions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_sessions_user_id_fkey");
        });

        modelBuilder.Entity<VectorIndex>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("vector_indexes_pkey");

            entity.ToTable("vector_indexes", "storage");

            entity.HasIndex(e => new { e.Name, e.BucketId }, "vector_indexes_name_bucket_id_idx")
                .IsUnique()
                .UseCollation(new[] { "C", null });

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.BucketId).HasColumnName("bucket_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DataType).HasColumnName("data_type");
            entity.Property(e => e.Dimension).HasColumnName("dimension");
            entity.Property(e => e.DistanceMetric).HasColumnName("distance_metric");
            entity.Property(e => e.MetadataConfiguration)
                .HasColumnType("jsonb")
                .HasColumnName("metadata_configuration");
            entity.Property(e => e.Name)
                .UseCollation("C")
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Bucket).WithMany(p => p.VectorIndices)
                .HasForeignKey(d => d.BucketId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("vector_indexes_bucket_id_fkey");
        });

        modelBuilder.Entity<Vendor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("vendors_pkey");

            entity.ToTable("vendors");

            entity.HasIndex(e => e.RestaurantId, "vendors_restaurant_id_idx");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Address).HasColumnName("address");
            entity.Property(e => e.ApiPortalUrl).HasColumnName("api_portal_url");
            entity.Property(e => e.ContactEmail).HasColumnName("contact_email");
            entity.Property(e => e.ContactName).HasColumnName("contact_name");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsIntegrated).HasColumnName("is_integrated");
            entity.Property(e => e.LeadTimeDays).HasColumnName("lead_time_days");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.PaymentTerms).HasColumnName("payment_terms");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Website).HasColumnName("website");

            entity.HasOne(d => d.Restaurant).WithMany(p => p.Vendors)
                .HasForeignKey(d => d.RestaurantId)
                .HasConstraintName("vendors_restaurant_id_fkey");
        });

        modelBuilder.Entity<VerificationToken>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("VerificationToken", "seo");

            entity.HasIndex(e => new { e.Identifier, e.Token }, "VerificationToken_identifier_token_key").IsUnique();

            entity.HasIndex(e => e.Token, "VerificationToken_token_key").IsUnique();

            entity.Property(e => e.Expires)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("expires");
            entity.Property(e => e.Identifier).HasColumnName("identifier");
            entity.Property(e => e.Token).HasColumnName("token");
        });

        modelBuilder.Entity<WebauthnChallenge>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("webauthn_challenges_pkey");

            entity.ToTable("webauthn_challenges", "auth");

            entity.HasIndex(e => e.ExpiresAt, "webauthn_challenges_expires_at_idx");

            entity.HasIndex(e => e.UserId, "webauthn_challenges_user_id_idx");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.ChallengeType).HasColumnName("challenge_type");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.SessionData)
                .HasColumnType("jsonb")
                .HasColumnName("session_data");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.WebauthnChallenges)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("webauthn_challenges_user_id_fkey");
        });

        modelBuilder.Entity<WebauthnCredential>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("webauthn_credentials_pkey");

            entity.ToTable("webauthn_credentials", "auth");

            entity.HasIndex(e => e.CredentialId, "webauthn_credentials_credential_id_key").IsUnique();

            entity.HasIndex(e => e.UserId, "webauthn_credentials_user_id_idx");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Aaguid).HasColumnName("aaguid");
            entity.Property(e => e.AttestationType)
                .HasDefaultValueSql("''::text")
                .HasColumnName("attestation_type");
            entity.Property(e => e.BackedUp).HasColumnName("backed_up");
            entity.Property(e => e.BackupEligible).HasColumnName("backup_eligible");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CredentialId).HasColumnName("credential_id");
            entity.Property(e => e.FriendlyName)
                .HasDefaultValueSql("''::text")
                .HasColumnName("friendly_name");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(e => e.PublicKey).HasColumnName("public_key");
            entity.Property(e => e.SignCount).HasColumnName("sign_count");
            entity.Property(e => e.Transports)
                .HasDefaultValueSql("'[]'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("transports");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.WebauthnCredentials)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("webauthn_credentials_user_id_fkey");
        });

        modelBuilder.Entity<WorkweekConfig>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("workweek_configs_pkey");

            entity.ToTable("workweek_configs");

            entity.HasIndex(e => e.RestaurantId, "workweek_configs_restaurant_id_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DayStartTime)
                .HasMaxLength(5)
                .HasDefaultValueSql("'04:00'::character varying")
                .HasColumnName("day_start_time");
            entity.Property(e => e.OvertimeMultiplier)
                .HasPrecision(3, 2)
                .HasDefaultValue(1.5m)
                .HasColumnName("overtime_multiplier");
            entity.Property(e => e.OvertimeThresholdHours)
                .HasPrecision(5, 2)
                .HasDefaultValue(40m)
                .HasColumnName("overtime_threshold_hours");
            entity.Property(e => e.RestaurantId).HasColumnName("restaurant_id");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("timestamp(3) without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.WeekStartDay).HasColumnName("week_start_day");
        });
        modelBuilder.HasSequence<int>("seq_schema_version", "graphql").IsCyclic();

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
