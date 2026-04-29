using GeekBackend.Api.Dtos;
using GeekBackend.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Api.Services;

public class DepartmentContentService
{
    private readonly AppDbContext _context;

    public DepartmentContentService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<DepartmentDto>> GetDepartmentContentAsync()
    {
        return await _context.Departments
            .AsNoTracking()
            .Include(d => d.UseCases)
                .ThenInclude(uc => uc.CaseStudy)
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentDto(d.Id, d.Name, d.Slug, d.Description, d.IconName, d.SortOrder)
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
            })
            .ToListAsync();
    }
}
