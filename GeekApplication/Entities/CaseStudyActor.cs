namespace GeekApplication.Entities;

public class CaseStudyActor
{
    public int Id { get; set; }
    public int CaseStudyId { get; set; }
    public required string ActorName { get; set; }
    public required string ActorRole { get; set; }
    public int SortOrder { get; set; }

    public virtual CaseStudy CaseStudy { get; set; } = null!;
    public virtual ICollection<CaseStudyEventFlowStep> CaseStudyEventFlowSteps { get; set; } = [];
}
