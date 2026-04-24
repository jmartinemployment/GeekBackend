using GeekBackend.Api.Dtos;
using GeekBackend.Data.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace GeekBackend.Api.Controllers;

[ApiController]
[Route("api/case-studies")]
public class CaseStudiesController : ControllerBase
{
    private readonly ICaseStudyRepository _caseStudies;

    public CaseStudiesController(ICaseStudyRepository caseStudies)
    {
        _caseStudies = caseStudies;
    }

    [HttpGet]
    public async Task<IReadOnlyList<CaseStudySummaryDto>> GetPublished()
    {
        var results = await _caseStudies.GetPublishedAsync();
        return results.Select(c => new CaseStudySummaryDto(
            c.Id, c.DescriptiveName, c.Slug, c.Status,
            c.ExecutiveSummary, c.PrimaryActor, c.PublishedAt)).ToList();
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<CaseStudyDetailDto>> GetBySlug(string slug)
    {
        var c = await _caseStudies.GetBySlugAsync(slug);
        if (c is null) return NotFound();

        return new CaseStudyDetailDto(
            c.Id, c.DescriptiveName, c.Slug, c.Status,
            c.ExecutiveSummary, c.PrimaryActor, c.Trigger,
            c.ProblemChallenge, c.Solution, c.PostConditions, c.Exceptions, c.IndustryCitation,
            c.CreatedAt, c.UpdatedAt, c.PublishedAt,
            c.CaseStudyActors.Select(a => new ActorDto(a.Id, a.ActorName, a.ActorRole, a.SortOrder)).ToList(),
            c.CaseStudyMetrics.Select(m => new MetricDto(m.Id, m.MetricLabel, m.MetricValue, m.MetricUnit, m.SortOrder)).ToList(),
            c.CaseStudyEventFlowSteps.Select(s => new EventFlowStepDto(s.Id, s.StepNumber, s.StepDescription, s.StepActor?.ActorName)).ToList());
    }
}
