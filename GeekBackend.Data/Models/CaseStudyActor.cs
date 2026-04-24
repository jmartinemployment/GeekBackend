using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CaseStudyActor
{
    public int Id { get; set; }
    public int CaseStudyId { get; set; }
    public string ActorName { get; set; } = null!;
    public string ActorRole { get; set; } = null!;
    public int SortOrder { get; set; }

    public virtual CaseStudy CaseStudy { get; set; } = null!;
    public virtual ICollection<CaseStudyEventFlowStep> CaseStudyEventFlowSteps { get; set; } = new List<CaseStudyEventFlowStep>();
}
