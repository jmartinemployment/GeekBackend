using GeekRepository.Data.Entities.ContentWriterV3;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GeekRepository.Data;

public class ContentWriterV3DbContext : DbContext
{
    public ContentWriterV3DbContext(DbContextOptions<ContentWriterV3DbContext> options)
        : base(options)
    {
    }

    // Workspace & Client
    public virtual DbSet<Workspace> Workspaces => Set<Workspace>();
    public virtual DbSet<Client> Clients => Set<Client>();
    public virtual DbSet<ClientProfile> ClientProfiles => Set<ClientProfile>();
    public virtual DbSet<ClientProfileVersion> ClientProfileVersions => Set<ClientProfileVersion>();
    public virtual DbSet<ClientBrandVoiceLink> ClientBrandVoiceLinks => Set<ClientBrandVoiceLink>();

    // Campaigns & Assets
    public virtual DbSet<ContentCampaign> ContentCampaigns => Set<ContentCampaign>();
    public virtual DbSet<ContentAsset> ContentAssets => Set<ContentAsset>();
    public virtual DbSet<ContentAssetVersion> ContentAssetVersions => Set<ContentAssetVersion>();

    // Research & Evidence
    public virtual DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();
    public virtual DbSet<ResearchSource> ResearchSources => Set<ResearchSource>();
    public virtual DbSet<ResearchEvidence> ResearchEvidences => Set<ResearchEvidence>();
    public virtual DbSet<PainPoint> PainPoints => Set<PainPoint>();
    public virtual DbSet<PainPointEvidenceLink> PainPointEvidenceLinks => Set<PainPointEvidenceLink>();
    public virtual DbSet<ReconciliationProposal> ReconciliationProposals => Set<ReconciliationProposal>();
    public virtual DbSet<KeywordCandidate> KeywordCandidates => Set<KeywordCandidate>();

    // Workflow
    public virtual DbSet<StrategyBrief> StrategyBriefs => Set<StrategyBrief>();
    public virtual DbSet<Publication> Publications => Set<Publication>();
    public virtual DbSet<Job> Jobs => Set<Job>();

    // Audit Trail
    public virtual DbSet<ApprovalEvent> ApprovalEvents => Set<ApprovalEvent>();
    public virtual DbSet<PublicationEvent> PublicationEvents => Set<PublicationEvent>();
    public virtual DbSet<ReviewComment> ReviewComments => Set<ReviewComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("content_writer_v3");

        // Workspaces & Clients
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.OwnerId).IsRequired().HasColumnName("owner_id");
            entity.Property(w => w.Name).IsRequired().HasMaxLength(256);
            entity.Property(w => w.CreatedAtUtc).IsRequired();
            entity.Property(w => w.UpdatedAtUtc).IsRequired();
            entity.HasIndex(w => w.OwnerId).HasDatabaseName("ix_workspaces_owner_id");
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.ToTable("clients");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.WorkspaceId).IsRequired();
            entity.Property(c => c.Name).IsRequired().HasMaxLength(256);
            entity.Property(c => c.CreatedAtUtc).IsRequired();
            entity.Property(c => c.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ClientProfile>(entity =>
        {
            entity.ToTable("client_profiles");
            entity.HasKey(cp => cp.Id);
            entity.Property(cp => cp.ClientId).IsRequired();
            entity.Property(cp => cp.Name).IsRequired().HasMaxLength(256);
            entity.Property(cp => cp.CreatedAtUtc).IsRequired();
            entity.Property(cp => cp.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ClientProfileVersion>(entity =>
        {
            entity.ToTable("client_profile_versions");
            entity.HasKey(cpv => cpv.Id);
            entity.Property(cpv => cpv.ProfileId).IsRequired();
            entity.Property(cpv => cpv.Version).IsRequired();
            entity.Property(cpv => cpv.ApprovedFacts)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new())
                .HasColumnName("approved_facts");
            entity.Property(cpv => cpv.ProhibitedClaims)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new())
                .HasColumnName("prohibited_claims");
            entity.Property(cpv => cpv.RowVersion).IsConcurrencyToken();
            entity.Property(cpv => cpv.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ClientBrandVoiceLink>(entity =>
        {
            entity.ToTable("client_brand_voice_links");
            entity.HasKey(cbvl => cbvl.Id);
            entity.Property(cbvl => cbvl.ProfileVersionId).IsRequired();
            entity.Property(cbvl => cbvl.BrandVoiceId).IsRequired();
            entity.Property(cbvl => cbvl.CreatedAtUtc).IsRequired();
        });

        // Campaigns & Assets
        modelBuilder.Entity<ContentCampaign>(entity =>
        {
            entity.ToTable("content_campaigns");
            entity.HasKey(cc => cc.Id);
            entity.Property(cc => cc.ClientId).IsRequired();
            entity.Property(cc => cc.Name).IsRequired().HasMaxLength(256);
            entity.Property(cc => cc.Keyword).IsRequired().HasMaxLength(512);
            entity.Property(cc => cc.Status).IsRequired().HasMaxLength(32);
            entity.Property(cc => cc.ProfileVersionId).IsRequired();
            entity.Property(cc => cc.RowVersion).IsConcurrencyToken();
            entity.Property(cc => cc.CreatedAtUtc).IsRequired();
            entity.Property(cc => cc.UpdatedAtUtc).IsRequired();
            entity.HasIndex(cc => new { cc.ClientId, cc.Keyword }).IsUnique().HasDatabaseName("ix_campaigns_client_keyword");
        });

        modelBuilder.Entity<ContentAsset>(entity =>
        {
            entity.ToTable("content_assets");
            entity.HasKey(ca => ca.Id);
            entity.Property(ca => ca.CampaignId).IsRequired();
            entity.Property(ca => ca.Type).IsRequired().HasMaxLength(32);
            entity.Property(ca => ca.Name).IsRequired().HasMaxLength(256);
            entity.Property(ca => ca.Status).IsRequired().HasMaxLength(32);
            entity.Property(ca => ca.CreatedAtUtc).IsRequired();
            entity.Property(ca => ca.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ContentAssetVersion>(entity =>
        {
            entity.ToTable("content_asset_versions");
            entity.HasKey(cav => cav.Id);
            entity.Property(cav => cav.AssetId).IsRequired();
            entity.Property(cav => cav.VersionNumber).IsRequired();
            entity.Property(cav => cav.BodyDocumentJson).IsRequired().HasColumnName("body_document_json");
            entity.Property(cav => cav.RowVersion).IsConcurrencyToken();
            entity.Property(cav => cav.CreatedAtUtc).IsRequired();
        });

        // Research & Evidence
        modelBuilder.Entity<ResearchRun>(entity =>
        {
            entity.ToTable("research_runs");
            entity.HasKey(rr => rr.Id);
            entity.Property(rr => rr.CampaignId).IsRequired();
            entity.Property(rr => rr.Keyword).IsRequired().HasMaxLength(512);
            entity.Property(rr => rr.Status).IsRequired().HasMaxLength(32);
            entity.Property(rr => rr.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ResearchSource>(entity =>
        {
            entity.ToTable("research_sources");
            entity.HasKey(rs => rs.Id);
            entity.Property(rs => rs.ResearchRunId).IsRequired();
            entity.Property(rs => rs.SourceType).IsRequired().HasMaxLength(32);
            entity.Property(rs => rs.Url).HasMaxLength(2048);
            entity.Property(rs => rs.Title).IsRequired().HasMaxLength(512);
            entity.Property(rs => rs.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ResearchEvidence>(entity =>
        {
            entity.ToTable("research_evidences");
            entity.HasKey(re => re.Id);
            entity.Property(re => re.ResearchSourceId).IsRequired();
            entity.Property(re => re.Statement).IsRequired();
            entity.Property(re => re.SupportLevel).IsRequired().HasMaxLength(32);
            entity.Property(re => re.Confidence).IsRequired();
            entity.Property(re => re.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<PainPoint>(entity =>
        {
            entity.ToTable("pain_points");
            entity.HasKey(pp => pp.Id);
            entity.Property(pp => pp.ClientId).IsRequired();
            entity.Property(pp => pp.Name).IsRequired().HasMaxLength(256);
            entity.Property(pp => pp.Description).IsRequired();
            entity.Property(pp => pp.ReaderSymptom).IsRequired();
            entity.Property(pp => pp.CostOfInaction).IsRequired();
            entity.Property(pp => pp.OfferTerminology).IsRequired();
            entity.Property(pp => pp.Objections)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new())
                .HasColumnName("objections");
            entity.Property(pp => pp.Confidence).IsRequired();
            entity.Property(pp => pp.CreatedAtUtc).IsRequired();
            entity.Property(pp => pp.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<PainPointEvidenceLink>(entity =>
        {
            entity.ToTable("pain_point_evidence_links");
            entity.HasKey(ppel => ppel.Id);
            entity.Property(ppel => ppel.PainPointId).IsRequired();
            entity.Property(ppel => ppel.ResearchEvidenceId).IsRequired();
            entity.Property(ppel => ppel.Dimension).IsRequired().HasMaxLength(32);
            entity.Property(ppel => ppel.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ReconciliationProposal>(entity =>
        {
            entity.ToTable("reconciliation_proposals");
            entity.HasKey(rp => rp.Id);
            entity.Property(rp => rp.ResearchRunId).IsRequired();
            entity.Property(rp => rp.ProposalType).IsRequired().HasMaxLength(32);
            entity.Property(rp => rp.ProposedData)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new())
                .HasColumnName("proposed_data");
            entity.Property(rp => rp.Status).IsRequired().HasMaxLength(32);
            entity.Property(rp => rp.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<KeywordCandidate>(entity =>
        {
            entity.ToTable("keyword_candidates");
            entity.HasKey(kc => kc.Id);
            entity.Property(kc => kc.ClientId).IsRequired();
            entity.Property(kc => kc.Keyword).IsRequired().HasMaxLength(512);
            entity.Property(kc => kc.Status).IsRequired().HasMaxLength(32);
            entity.Property(kc => kc.CreatedAtUtc).IsRequired();
            entity.Property(kc => kc.UpdatedAtUtc).IsRequired();
        });

        // Workflow
        modelBuilder.Entity<StrategyBrief>(entity =>
        {
            entity.ToTable("strategy_briefs");
            entity.HasKey(sb => sb.Id);
            entity.Property(sb => sb.CampaignId).IsRequired();
            entity.Property(sb => sb.PainPointId).IsRequired();
            entity.Property(sb => sb.AudienceProfile).IsRequired();
            entity.Property(sb => sb.BuyingStage).IsRequired();
            entity.Property(sb => sb.Angle).IsRequired();
            entity.Property(sb => sb.CallToAction).IsRequired();
            entity.Property(sb => sb.Status).IsRequired().HasMaxLength(32);
            entity.Property(sb => sb.RowVersion).IsConcurrencyToken();
            entity.Property(sb => sb.CreatedAtUtc).IsRequired();
            entity.Property(sb => sb.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<Publication>(entity =>
        {
            entity.ToTable("publications");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.AssetVersionId).IsRequired();
            entity.Property(p => p.Status).IsRequired().HasMaxLength(32);
            entity.Property(p => p.ResponseSnapshot)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null))
                .HasColumnName("response_snapshot");
            entity.Property(p => p.CreatedAtUtc).IsRequired();
            entity.Property(p => p.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.ToTable("jobs");
            entity.HasKey(j => j.Id);
            entity.Property(j => j.JobType).IsRequired().HasMaxLength(64);
            entity.Property(j => j.Status).IsRequired().HasMaxLength(32);
            entity.Property(j => j.PayloadJson)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new())
                .HasColumnName("payload_json");
            entity.Property(j => j.IdempotencyKey).IsRequired().HasMaxLength(256);
            entity.Property(j => j.Status).IsRequired().HasMaxLength(32);
            entity.Property(j => j.CreatedAtUtc).IsRequired();
            entity.HasIndex(j => j.IdempotencyKey).IsUnique().HasDatabaseName("ix_jobs_idempotency_key");
            entity.HasIndex(j => new { j.Status, j.LeaseExpiresAtUtc }).HasDatabaseName("ix_jobs_status_lease");
        });

        // Audit Trail
        modelBuilder.Entity<ApprovalEvent>(entity =>
        {
            entity.ToTable("approval_events");
            entity.HasKey(ae => ae.Id);
            entity.Property(ae => ae.AssetVersionId).IsRequired();
            entity.Property(ae => ae.UserId).IsRequired();
            entity.Property(ae => ae.Action).IsRequired().HasMaxLength(32);
            entity.Property(ae => ae.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<PublicationEvent>(entity =>
        {
            entity.ToTable("publication_events");
            entity.HasKey(pe => pe.Id);
            entity.Property(pe => pe.PublicationId).IsRequired();
            entity.Property(pe => pe.UserId).IsRequired();
            entity.Property(pe => pe.Status).IsRequired().HasMaxLength(32);
            entity.Property(pe => pe.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ReviewComment>(entity =>
        {
            entity.ToTable("review_comments");
            entity.HasKey(rc => rc.Id);
            entity.Property(rc => rc.AssetVersionId).IsRequired();
            entity.Property(rc => rc.UserId).IsRequired();
            entity.Property(rc => rc.Content).IsRequired();
            entity.Property(rc => rc.Resolution).HasMaxLength(32);
            entity.Property(rc => rc.CreatedAtUtc).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
