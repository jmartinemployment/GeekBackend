namespace GeekApplication.Entities;

public class CaseStudy
{
    public int Id { get; set; }
    public required string DescriptiveName { get; set; }
    public required string Slug { get; set; }
    public required string Status { get; set; }
    public required string ExecutiveSummary { get; set; }
    public string? Subtitle { get; set; }
    public required string PrimaryActor { get; set; }
    public required string Trigger { get; set; }
    public required string ProblemChallenge { get; set; }
    public required string Solution { get; set; }
    public string? PostConditions { get; set; }
    public string? Exceptions { get; set; }
    public string? IndustryCitation { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }

    public virtual ICollection<UseCase> UseCases { get; set; } = [];
    public virtual ICollection<CaseStudyMetric> CaseStudyMetrics { get; set; } = [];
    public virtual ICollection<CaseStudyActor> CaseStudyActors { get; set; } = [];
    public virtual ICollection<CaseStudyEventFlowStep> CaseStudyEventFlowSteps { get; set; } = [];
}
