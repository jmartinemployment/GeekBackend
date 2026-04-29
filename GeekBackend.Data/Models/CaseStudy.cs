using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CaseStudy
{
    public int Id { get; set; }
    public string DescriptiveName { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string ExecutiveSummary { get; set; } = null!;
    public string? Subtitle { get; set; }
    public string PrimaryActor { get; set; } = null!;
    public string Trigger { get; set; } = null!;
    public string ProblemChallenge { get; set; } = null!;
    public string Solution { get; set; } = null!;
    public string? PostConditions { get; set; }
    public string? Exceptions { get; set; }
    public string? IndustryCitation { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }

    public virtual ICollection<UseCase> UseCases { get; set; } = new List<UseCase>();
    public virtual ICollection<CaseStudyMetric> CaseStudyMetrics { get; set; } = new List<CaseStudyMetric>();
    public virtual ICollection<CaseStudyActor> CaseStudyActors { get; set; } = new List<CaseStudyActor>();
    public virtual ICollection<CaseStudyEventFlowStep> CaseStudyEventFlowSteps { get; set; } = new List<CaseStudyEventFlowStep>();
}
