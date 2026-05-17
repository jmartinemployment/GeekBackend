using GeekAPI.Dtos;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers;

[ApiController]
[Route("api/case-studies")]
public class CaseStudiesController : ControllerBase
{
    private readonly ICaseStudyRepository _caseStudies;

    public CaseStudiesController(ICaseStudyRepository caseStudies)
    {
        _caseStudies = caseStudies;
    }

    // ── Case Studies ─────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IReadOnlyList<CaseStudySummaryDto>> GetPublished()
    {
        var results = await _caseStudies.GetPublishedAsync();
        return results.Select(ToSummaryDto).ToList();
    }

    [HttpGet("all")]
    public async Task<IReadOnlyList<CaseStudySummaryDto>> GetAll()
    {
        var results = await _caseStudies.GetAllAsync();
        return results.Select(ToSummaryDto).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CaseStudyDetailDto>> GetById(int id)
    {
        var c = await _caseStudies.GetByIdAsync(id);
        if (c is null) return NotFound();
        return ToDetailDto(c);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<CaseStudyDetailDto>> GetBySlug(string slug)
    {
        var c = await _caseStudies.GetBySlugAsync(slug);
        if (c is null) return NotFound();
        return ToDetailDto(c);
    }

    [HttpPost]
    public async Task<ActionResult<CaseStudyDetailDto>> Create(CaseStudyRequest req)
    {
        var caseStudy = new CaseStudy
        {
            DescriptiveName = req.DescriptiveName,
            Slug = req.Slug,
            Status = req.Status,
            ExecutiveSummary = req.ExecutiveSummary ?? string.Empty,
            Subtitle = req.Subtitle,
            PrimaryActor = req.PrimaryActor ?? string.Empty,
            Trigger = req.Trigger ?? string.Empty,
            ProblemChallenge = req.ProblemChallenge ?? string.Empty,
            Solution = req.Solution ?? string.Empty,
            PostConditions = req.PostConditions,
            Exceptions = req.Exceptions,
            IndustryCitation = req.IndustryCitation
        };

        await _caseStudies.AddAsync(caseStudy);
        return CreatedAtAction(nameof(GetById), new { id = caseStudy.Id }, ToDetailDto(caseStudy));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CaseStudyDetailDto>> Update(int id, CaseStudyRequest req)
    {
        var caseStudy = await _caseStudies.GetByIdAsync(id);
        if (caseStudy is null) return NotFound();

        caseStudy.DescriptiveName = req.DescriptiveName;
        caseStudy.Slug = req.Slug;
        caseStudy.Status = req.Status;
        caseStudy.ExecutiveSummary = req.ExecutiveSummary ?? string.Empty;
        caseStudy.Subtitle = req.Subtitle;
        caseStudy.PrimaryActor = req.PrimaryActor ?? string.Empty;
        caseStudy.Trigger = req.Trigger ?? string.Empty;
        caseStudy.ProblemChallenge = req.ProblemChallenge ?? string.Empty;
        caseStudy.Solution = req.Solution ?? string.Empty;
        caseStudy.PostConditions = req.PostConditions;
        caseStudy.Exceptions = req.Exceptions;
        caseStudy.IndustryCitation = req.IndustryCitation;

        await _caseStudies.UpdateAsync(caseStudy);
        return ToDetailDto(caseStudy);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var caseStudy = await _caseStudies.GetByIdAsync(id);
        if (caseStudy is null) return NotFound();
        await _caseStudies.DeleteAsync(id);
        return NoContent();
    }

    // ── Actors ───────────────────────────────────────────────────────────────

    [HttpPost("{id:int}/actors")]
    public async Task<ActionResult<ActorDto>> AddActor(int id, ActorRequest req)
    {
        var caseStudy = await _caseStudies.GetByIdAsync(id);
        if (caseStudy is null) return NotFound();

        var actor = new CaseStudyActor
        {
            CaseStudyId = id,
            ActorName = req.ActorName,
            ActorRole = req.ActorRole,
            SortOrder = req.SortOrder
        };

        var result = await _caseStudies.AddActorAsync(id, actor);
        return CreatedAtAction(nameof(GetById), new { id }, new ActorDto(result.Id, result.ActorName, result.ActorRole, result.SortOrder));
    }

    [HttpPut("{id:int}/actors/{actorId:int}")]
    public async Task<ActionResult<ActorDto>> UpdateActor(int id, int actorId, ActorRequest req)
    {
        var actor = await _caseStudies.UpdateActorAsync(id, actorId, req.ActorName, req.ActorRole, req.SortOrder);
        if (actor is null) return NotFound();
        return new ActorDto(actor.Id, actor.ActorName, actor.ActorRole, actor.SortOrder);
    }

    [HttpDelete("{id:int}/actors/{actorId:int}")]
    public async Task<IActionResult> DeleteActor(int id, int actorId)
    {
        var existing = await _caseStudies.GetActorAsync(id, actorId);
        if (existing is null) return NotFound();
        await _caseStudies.DeleteActorAsync(id, actorId);
        return NoContent();
    }

    // ── Metrics ──────────────────────────────────────────────────────────────

    [HttpPost("{id:int}/metrics")]
    public async Task<ActionResult<MetricDto>> AddMetric(int id, MetricRequest req)
    {
        var caseStudy = await _caseStudies.GetByIdAsync(id);
        if (caseStudy is null) return NotFound();

        var metric = new CaseStudyMetric
        {
            CaseStudyId = id,
            MetricLabel = req.MetricLabel,
            MetricValue = req.MetricValue,
            MetricUnit = req.MetricUnit,
            SortOrder = req.SortOrder
        };

        var result = await _caseStudies.AddMetricAsync(id, metric);
        return CreatedAtAction(nameof(GetById), new { id }, new MetricDto(result.Id, result.MetricLabel, result.MetricValue, result.MetricUnit, result.SortOrder));
    }

    [HttpPut("{id:int}/metrics/{metricId:int}")]
    public async Task<ActionResult<MetricDto>> UpdateMetric(int id, int metricId, MetricRequest req)
    {
        var metric = await _caseStudies.UpdateMetricAsync(id, metricId, req.MetricLabel, req.MetricValue, req.MetricUnit, req.SortOrder);
        if (metric is null) return NotFound();
        return new MetricDto(metric.Id, metric.MetricLabel, metric.MetricValue, metric.MetricUnit, metric.SortOrder);
    }

    [HttpDelete("{id:int}/metrics/{metricId:int}")]
    public async Task<IActionResult> DeleteMetric(int id, int metricId)
    {
        var existing = await _caseStudies.GetMetricAsync(id, metricId);
        if (existing is null) return NotFound();
        await _caseStudies.DeleteMetricAsync(id, metricId);
        return NoContent();
    }

    // ── Event Flow Steps ─────────────────────────────────────────────────────

    [HttpPost("{id:int}/event-flow-steps")]
    public async Task<ActionResult<EventFlowStepDto>> AddEventFlowStep(int id, EventFlowStepRequest req)
    {
        var caseStudy = await _caseStudies.GetByIdAsync(id);
        if (caseStudy is null) return NotFound();

        var step = new CaseStudyEventFlowStep
        {
            CaseStudyId = id,
            StepNumber = req.StepNumber,
            StepDescription = req.StepDescription,
            StepActorId = req.StepActorId
        };

        var result = await _caseStudies.AddEventFlowStepAsync(id, step);
        return CreatedAtAction(nameof(GetById), new { id }, new EventFlowStepDto(result.Id, result.StepNumber, result.StepDescription, result.StepActor?.ActorName));
    }

    [HttpPut("{id:int}/event-flow-steps/{stepId:int}")]
    public async Task<ActionResult<EventFlowStepDto>> UpdateEventFlowStep(int id, int stepId, EventFlowStepRequest req)
    {
        var step = await _caseStudies.UpdateEventFlowStepAsync(id, stepId, req.StepNumber, req.StepDescription, req.StepActorId);
        if (step is null) return NotFound();
        return new EventFlowStepDto(step.Id, step.StepNumber, step.StepDescription, step.StepActor?.ActorName);
    }

    [HttpDelete("{id:int}/event-flow-steps/{stepId:int}")]
    public async Task<IActionResult> DeleteEventFlowStep(int id, int stepId)
    {
        var existing = await _caseStudies.GetEventFlowStepAsync(id, stepId);
        if (existing is null) return NotFound();
        await _caseStudies.DeleteEventFlowStepAsync(id, stepId);
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CaseStudySummaryDto ToSummaryDto(CaseStudy c) =>
        new(c.Id, c.DescriptiveName, c.Slug, c.Status, c.ExecutiveSummary, c.Subtitle, c.PrimaryActor, c.PublishedAt);

    private static CaseStudyDetailDto ToDetailDto(CaseStudy c) =>
        new(c.Id, c.DescriptiveName, c.Slug, c.Status,
            c.ExecutiveSummary, c.Subtitle, c.PrimaryActor, c.Trigger,
            c.ProblemChallenge, c.Solution, c.PostConditions, c.Exceptions, c.IndustryCitation,
            c.CreatedAt.GetValueOrDefault(), c.UpdatedAt, c.PublishedAt,
            c.CaseStudyActors.OrderBy(a => a.SortOrder).Select(a => new ActorDto(a.Id, a.ActorName, a.ActorRole, a.SortOrder)).ToList(),
            c.CaseStudyMetrics.OrderBy(m => m.SortOrder).Select(m => new MetricDto(m.Id, m.MetricLabel, m.MetricValue, m.MetricUnit, m.SortOrder)).ToList(),
            c.CaseStudyEventFlowSteps.OrderBy(s => s.StepNumber).Select(s => new EventFlowStepDto(s.Id, s.StepNumber, s.StepDescription, s.StepActor?.ActorName)).ToList());
}
