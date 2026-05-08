namespace GeekApplication.Entities;

public class CaseStudyMetric
{
    public int Id { get; set; }
    public int CaseStudyId { get; set; }
    public required string MetricLabel { get; set; }
    public required string MetricValue { get; set; }
    public string? MetricUnit { get; set; }
    public int SortOrder { get; set; }

    public virtual CaseStudy CaseStudy { get; set; } = null!;
}
