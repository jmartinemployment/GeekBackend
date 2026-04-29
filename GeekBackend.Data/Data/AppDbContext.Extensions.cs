using GeekBackend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Data.Data;

public partial class AppDbContext
{
    public virtual DbSet<Department> Departments { get; set; }
    public virtual DbSet<CaseStudy> CaseStudies { get; set; }
    public virtual DbSet<UseCase> UseCases { get; set; }
    public virtual DbSet<CaseStudyMetric> CaseStudyMetrics { get; set; }
    public virtual DbSet<CaseStudyActor> CaseStudyActors { get; set; }
    public virtual DbSet<CaseStudyEventFlowStep> CaseStudyEventFlowSteps { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("departments");
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(e => e.Name).HasMaxLength(100).HasColumnName("name");
            entity.Property(e => e.Slug).HasMaxLength(100).HasColumnName("slug");
            entity.Property(e => e.Description).HasColumnType("text").HasColumnName("description");
            entity.Property(e => e.IconName).HasMaxLength(100).HasColumnName("icon_name");
            entity.Property(e => e.SortOrder).HasDefaultValue(0).HasColumnName("sort_order");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        modelBuilder.Entity<CaseStudy>(entity =>
        {
            entity.ToTable("case_studies");
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(e => e.DescriptiveName).HasMaxLength(255).HasColumnName("descriptive_name");
            entity.Property(e => e.Slug).HasMaxLength(255).HasColumnName("slug");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("draft").HasColumnName("status");
            entity.Property(e => e.ExecutiveSummary).HasColumnType("text").HasColumnName("executive_summary");
            entity.Property(e => e.Subtitle).HasColumnType("text").HasColumnName("subtitle");
            entity.Property(e => e.PrimaryActor).HasMaxLength(150).HasColumnName("primary_actor");
            entity.Property(e => e.Trigger).HasColumnType("text").HasColumnName("trigger");
            entity.Property(e => e.ProblemChallenge).HasColumnType("text").HasColumnName("problem_challenge");
            entity.Property(e => e.Solution).HasColumnType("text").HasColumnName("solution");
            entity.Property(e => e.PostConditions).HasColumnType("text").HasColumnName("post_conditions");
            entity.Property(e => e.Exceptions).HasColumnType("text").HasColumnName("exceptions");
            entity.Property(e => e.IndustryCitation).HasMaxLength(150).HasColumnName("industry_citation");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            entity.Property(e => e.PublishedAt).HasColumnType("timestamp with time zone").HasColumnName("published_at");
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        modelBuilder.Entity<CaseStudyMetric>(entity =>
        {
            entity.ToTable("case_study_metrics");
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(e => e.CaseStudyId).HasColumnName("case_study_id");
            entity.Property(e => e.MetricLabel).HasMaxLength(150).HasColumnName("metric_label");
            entity.Property(e => e.MetricValue).HasMaxLength(50).HasColumnName("metric_value");
            entity.Property(e => e.MetricUnit).HasMaxLength(50).HasColumnName("metric_unit");
            entity.Property(e => e.SortOrder).HasDefaultValue(0).HasColumnName("sort_order");
            entity.HasOne(e => e.CaseStudy)
                .WithMany(c => c.CaseStudyMetrics)
                .HasForeignKey(e => e.CaseStudyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CaseStudyActor>(entity =>
        {
            entity.ToTable("case_study_actors");
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(e => e.CaseStudyId).HasColumnName("case_study_id");
            entity.Property(e => e.ActorName).HasMaxLength(150).HasColumnName("actor_name");
            entity.Property(e => e.ActorRole).HasMaxLength(50).HasColumnName("actor_role");
            entity.Property(e => e.SortOrder).HasDefaultValue(0).HasColumnName("sort_order");
            entity.HasOne(e => e.CaseStudy)
                .WithMany(c => c.CaseStudyActors)
                .HasForeignKey(e => e.CaseStudyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CaseStudyEventFlowStep>(entity =>
        {
            entity.ToTable("case_study_event_flow_steps");
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(e => e.CaseStudyId).HasColumnName("case_study_id");
            entity.Property(e => e.StepNumber).HasColumnName("step_number");
            entity.Property(e => e.StepDescription).HasColumnType("text").HasColumnName("step_description");
            entity.Property(e => e.StepActorId).HasColumnName("step_actor_id");
            entity.HasIndex(e => new { e.CaseStudyId, e.StepNumber }).IsUnique();
            entity.HasOne(e => e.CaseStudy)
                .WithMany(c => c.CaseStudyEventFlowSteps)
                .HasForeignKey(e => e.CaseStudyId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.StepActor)
                .WithMany(a => a.CaseStudyEventFlowSteps)
                .HasForeignKey(e => e.StepActorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<UseCase>(entity =>
        {
            entity.ToTable("use_cases");
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.CaseStudyId).HasColumnName("case_study_id");
            entity.Property(e => e.DescriptiveName).HasMaxLength(255).HasColumnName("descriptive_name");
            entity.Property(e => e.Slug).HasMaxLength(255).HasColumnName("slug");
            entity.Property(e => e.Summary).HasColumnType("text").HasColumnName("summary");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasOne(e => e.Department)
                .WithMany(d => d.UseCases)
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CaseStudy)
                .WithMany(c => c.UseCases)
                .HasForeignKey(e => e.CaseStudyId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
