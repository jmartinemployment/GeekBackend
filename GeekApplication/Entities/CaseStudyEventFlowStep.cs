namespace GeekApplication.Entities;

public class CaseStudyEventFlowStep
{
    public int Id { get; set; }
    public int CaseStudyId { get; set; }
    public int StepNumber { get; set; }
    public required string StepDescription { get; set; }
    public int? StepActorId { get; set; }

    public virtual CaseStudy CaseStudy { get; set; } = null!;
    public virtual CaseStudyActor? StepActor { get; set; }
}
