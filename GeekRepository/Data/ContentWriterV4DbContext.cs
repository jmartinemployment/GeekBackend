using GeekRepository.Data.Entities.ContentWriterV4;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Data;

public class ContentWriterV4DbContext : DbContext
{
    public ContentWriterV4DbContext(DbContextOptions<ContentWriterV4DbContext> options) : base(options)
    {
    }

    public virtual DbSet<Template> Templates => Set<Template>();
    public virtual DbSet<BrandVoice> BrandVoices => Set<BrandVoice>();
    public virtual DbSet<SocialScheduleEntry> SocialScheduleEntries => Set<SocialScheduleEntry>();
    public virtual DbSet<Document> Documents => Set<Document>();
    public virtual DbSet<Generation> Generations => Set<Generation>();
    public virtual DbSet<ProviderModel> ProviderModels => Set<ProviderModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("content_writer_v4");

        modelBuilder.Entity<Template>(entity =>
        {
            entity.ToTable("templates");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Slug).IsRequired().HasMaxLength(128);
            entity.HasIndex(t => t.Slug).IsUnique();
            entity.Property(t => t.Name).IsRequired().HasMaxLength(256);
            entity.Property(t => t.Description).IsRequired();
            entity.Property(t => t.Category).IsRequired().HasMaxLength(128);
            entity.Property(t => t.Icon).IsRequired().HasMaxLength(16);
            entity.Property(t => t.InputSchemaJson).IsRequired().HasColumnName("input_schema").HasColumnType("jsonb");
            entity.Property(t => t.SystemPrompt).IsRequired().HasColumnName("system_prompt");
            entity.Property(t => t.UserPromptTemplate).IsRequired().HasColumnName("user_prompt_template");
            entity.Property(t => t.IsActive).IsRequired().HasColumnName("is_active");
            entity.Property(t => t.CreatedAtUtc).IsRequired().HasColumnName("created_at");
        });

        modelBuilder.Entity<BrandVoice>(entity =>
        {
            entity.ToTable("brand_voices");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.OwnerId).IsRequired().HasColumnName("owner_id");
            entity.Property(b => b.Name).IsRequired().HasMaxLength(256);
            entity.Property(b => b.Description).IsRequired();
            entity.Property(b => b.Tone).IsRequired().HasMaxLength(128);
            entity.Property(b => b.SampleText).IsRequired().HasColumnName("sample_text");
            entity.Property(b => b.CreatedAtUtc).IsRequired().HasColumnName("created_at");
            entity.Property(b => b.UpdatedAtUtc).IsRequired().HasColumnName("updated_at");
            entity.HasIndex(b => b.OwnerId);
        });

        modelBuilder.Entity<SocialScheduleEntry>(entity =>
        {
            entity.ToTable("social_schedule_entries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OwnerId).IsRequired().HasColumnName("owner_id");
            entity.Property(e => e.CampaignId).IsRequired().HasColumnName("campaign_id");
            entity.Property(e => e.AssetId).IsRequired().HasColumnName("asset_id");
            entity.Property(e => e.AssetVersionId).IsRequired().HasColumnName("asset_version_id");
            entity.Property(e => e.Channel).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ScheduledAtUtc).IsRequired().HasColumnName("scheduled_at");
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(512);
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.CreatedAtUtc).IsRequired().HasColumnName("created_at");
            entity.Property(e => e.UpdatedAtUtc).IsRequired().HasColumnName("updated_at");
            entity.HasIndex(e => e.OwnerId);
            entity.HasIndex(e => new { e.OwnerId, e.ScheduledAtUtc })
                .HasDatabaseName("ix_social_schedule_owner_when");
            entity.HasIndex(e => new { e.CampaignId, e.ScheduledAtUtc })
                .HasDatabaseName("ix_social_schedule_campaign_when");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.OwnerId).IsRequired().HasColumnName("owner_id");
            entity.Property(d => d.TemplateId).HasColumnName("template_id");
            entity.Property(d => d.BrandVoiceId).HasColumnName("brand_voice_id");
            entity.Property(d => d.Title).IsRequired().HasMaxLength(512);
            entity.Property(d => d.InputsJson).IsRequired().HasColumnName("inputs").HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property(d => d.Content).IsRequired().HasColumnName("content").HasDefaultValue("");
            entity.Property(d => d.CreatedAtUtc).IsRequired().HasColumnName("created_at");
            entity.Property(d => d.UpdatedAtUtc).IsRequired().HasColumnName("updated_at");
            entity.HasIndex(d => d.OwnerId).HasDatabaseName("ix_documents_owner_id");
            entity.HasOne(d => d.Template).WithMany().HasForeignKey(d => d.TemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(d => d.BrandVoice).WithMany().HasForeignKey(d => d.BrandVoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Generation>(entity =>
        {
            entity.ToTable("generations");
            entity.HasKey(g => g.Id);
            entity.Property(g => g.DocumentId).HasColumnName("document_id");
            entity.Property(g => g.TemplateId).IsRequired().HasColumnName("template_id");
            entity.Property(g => g.BrandVoiceId).HasColumnName("brand_voice_id");
            entity.Property(g => g.Provider).IsRequired().HasMaxLength(64);
            entity.Property(g => g.Model).IsRequired().HasMaxLength(128);
            entity.Property(g => g.InputsJson).IsRequired().HasColumnName("inputs").HasColumnType("jsonb");
            entity.Property(g => g.Output).IsRequired();
            entity.Property(g => g.InputTokens).IsRequired().HasColumnName("input_tokens").HasDefaultValue(0);
            entity.Property(g => g.OutputTokens).IsRequired().HasColumnName("output_tokens").HasDefaultValue(0);
            entity.Property(g => g.CostUsd).IsRequired().HasColumnName("cost_usd").HasColumnType("numeric(10,6)").HasDefaultValue(0m);
            entity.Property(g => g.CreatedAtUtc).IsRequired().HasColumnName("created_at");
            entity.HasIndex(g => g.DocumentId).HasDatabaseName("ix_generations_document_id");
            entity.HasIndex(g => new { g.TemplateId, g.CreatedAtUtc }).HasDatabaseName("ix_generations_tmpl_created");
            entity.HasOne(g => g.Document).WithMany().HasForeignKey(g => g.DocumentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(g => g.Template).WithMany().HasForeignKey(g => g.TemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(g => g.BrandVoice).WithMany().HasForeignKey(g => g.BrandVoiceId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProviderModel>(entity =>
        {
            entity.ToTable("provider_models");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Provider).IsRequired().HasMaxLength(64);
            entity.Property(p => p.Model).IsRequired().HasMaxLength(128);
            entity.Property(p => p.InputCostPer1K).IsRequired().HasColumnName("input_cost_per_1k").HasColumnType("numeric(10,6)");
            entity.Property(p => p.OutputCostPer1K).IsRequired().HasColumnName("output_cost_per_1k").HasColumnType("numeric(10,6)");
            entity.Property(p => p.IsActive).IsRequired().HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(p => p.EffectiveAtUtc).IsRequired().HasColumnName("effective_at");
            entity.HasIndex(p => new { p.Provider, p.Model }).IsUnique();
        });
    }
}
