using GeekAPI.Dtos;
using GeekApplication.Interfaces;

namespace GeekAPI.Services;

public class DepartmentContentService
{
    private readonly IDepartmentRepository _departments;

    public DepartmentContentService(IDepartmentRepository departments)
    {
        _departments = departments;
    }

    public async Task<List<DepartmentDto>> GetDepartmentContentAsync()
    {
        var departments = await _departments.GetWithUseCasesAndCaseStudiesAsync();
        return departments.Select(d => new DepartmentDto(d.Id, d.Name, d.Slug, d.Description, d.IconName, d.SortOrder)
        {
            UseCases = d.UseCases
                .Where(uc => uc.CaseStudy != null && uc.CaseStudy.Status == "published")
                .OrderBy(uc => uc.DescriptiveName)
                .Select(uc => new UseCaseDto(uc.Id, uc.DescriptiveName, uc.Slug, uc.Summary, uc.CaseStudyId)
                {
                    CaseStudy = new CaseStudyDto
                    {
                        Id = uc.CaseStudy!.Id,
                        DescriptiveName = uc.CaseStudy.DescriptiveName,
                        Slug = uc.CaseStudy.Slug,
                        ExecutiveSummary = uc.CaseStudy.ExecutiveSummary,
                        Subtitle = uc.CaseStudy.Subtitle,
                        PrimaryActor = uc.CaseStudy.PrimaryActor,
                        Trigger = uc.CaseStudy.Trigger,
                        ProblemChallenge = uc.CaseStudy.ProblemChallenge,
                        Solution = uc.CaseStudy.Solution,
                        PostConditions = uc.CaseStudy.PostConditions,
                        IndustryCitation = uc.CaseStudy.IndustryCitation
                    }
                })
                .ToList()
        }).ToList();
    }
}
