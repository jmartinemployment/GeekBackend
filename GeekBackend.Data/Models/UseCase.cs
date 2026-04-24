using System;

namespace GeekBackend.Data.Models;

public partial class UseCase
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public int? CaseStudyId { get; set; }
    public string DescriptiveName { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Department Department { get; set; } = null!;
    public virtual CaseStudy? CaseStudy { get; set; }
}
