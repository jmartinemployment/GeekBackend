
namespace GeekBackend.Api.Dtos;

public class UseCaseDto
{
    public int Id { get; set; }
    public string DescriptiveName { get; set; }
    public string Slug { get; set; }
    public string Summary { get; set; }
    public int CaseStudyId { get; set; }
    public CaseStudyDto CaseStudy { get; set; } = null!;

    public UseCaseDto(int id, string descriptiveName, string slug, string summary, int caseStudyId)
    {
        Id = id;
        DescriptiveName = descriptiveName;
        Slug = slug;
        Summary = summary;
        CaseStudyId = caseStudyId;
    }
}

