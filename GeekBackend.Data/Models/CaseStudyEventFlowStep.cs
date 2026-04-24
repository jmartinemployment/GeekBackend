namespace GeekBackend.Data.Models;

public partial class CaseStudyEventFlowStep
{
    public int Id { get; set; }
    public int CaseStudyId { get; set; }
    public int StepNumber { get; set; }
    public string StepDescription { get; set; } = null!;
    public int? StepActorId { get; set; }

    public virtual CaseStudy CaseStudy { get; set; } = null!;
    public virtual CaseStudyActor? StepActor { get; set; }
}
