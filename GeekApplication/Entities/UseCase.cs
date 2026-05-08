namespace GeekApplication.Entities;

public class UseCase
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public int CaseStudyId { get; set; }
    public required string DescriptiveName { get; set; }
    public required string Slug { get; set; }
    public required string Summary { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Department Department { get; set; } = null!;
    public virtual CaseStudy CaseStudy { get; set; } = null!;
}
