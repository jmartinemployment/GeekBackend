namespace GeekBackend.Data.Models;

public partial class CaseStudyMetric
{
    public int Id { get; set; }
    public int CaseStudyId { get; set; }
    public string MetricLabel { get; set; } = null!;
    public string MetricValue { get; set; } = null!;
    public string? MetricUnit { get; set; }
    public int SortOrder { get; set; }

    public virtual CaseStudy CaseStudy { get; set; } = null!;
}
