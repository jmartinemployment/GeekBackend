using GeekBackend.Api.Dtos;
using GeekBackend.Data.Data;
using GeekBackend.Data.Models;
using GeekBackend.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekBackend.Api.Controllers;

[ApiController]
[Route("api/case-studies")]
public class CaseStudiesController : ControllerBase
{
    private readonly ICaseStudyRepository _caseStudies;
    private readonly AppDbContext _db;

    public CaseStudiesController(ICaseStudyRepository caseStudies, AppDbContext db)
    {
        _caseStudies = caseStudies;
        _db = db;
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
            ExecutiveSummary = req.ExecutiveSummary,
            Subtitle = req.Subtitle,
            PrimaryActor = req.PrimaryActor,
            Trigger = req.Trigger,
            ProblemChallenge = req.ProblemChallenge,
            Solution = req.Solution,
            PostConditions = req.PostConditions,
            Exceptions = req.Exceptions,
            IndustryCitation = req.IndustryCitation,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            PublishedAt = req.Status == "published" ? DateTime.UtcNow : null
        };

        await _caseStudies.AddAsync(caseStudy);
        return CreatedAtAction(nameof(GetById), new { id = caseStudy.Id }, ToDetailDto(caseStudy));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CaseStudyDetailDto>> Update(int id, CaseStudyRequest req)
    {
        var caseStudy = await _caseStudies.GetByIdAsync(id);
        if (caseStudy is null) return NotFound();

        var wasPublished = caseStudy.Status == "published";

        caseStudy.DescriptiveName = req.DescriptiveName;
        caseStudy.Slug = req.Slug;
        caseStudy.Status = req.Status;
        caseStudy.ExecutiveSummary = req.ExecutiveSummary;
        caseStudy.Subtitle = req.Subtitle;
        caseStudy.PrimaryActor = req.PrimaryActor;
        caseStudy.Trigger = req.Trigger;
        caseStudy.ProblemChallenge = req.ProblemChallenge;
        caseStudy.Solution = req.Solution;
        caseStudy.PostConditions = req.PostConditions;
        caseStudy.Exceptions = req.Exceptions;
        caseStudy.IndustryCitation = req.IndustryCitation;
        caseStudy.UpdatedAt = DateTime.UtcNow;

        if (!wasPublished && req.Status == "published")
            caseStudy.PublishedAt = DateTime.UtcNow;

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

        await _db.CaseStudyActors.AddAsync(actor);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id }, new ActorDto(actor.Id, actor.ActorName, actor.ActorRole, actor.SortOrder));
    }

    [HttpPut("{id:int}/actors/{actorId:int}")]
    public async Task<ActionResult<ActorDto>> UpdateActor(int id, int actorId, ActorRequest req)
    {
        var actor = await _db.CaseStudyActors.FirstOrDefaultAsync(a => a.Id == actorId && a.CaseStudyId == id);
        if (actor is null) return NotFound();

        actor.ActorName = req.ActorName;
        actor.ActorRole = req.ActorRole;
        actor.SortOrder = req.SortOrder;

        await _db.SaveChangesAsync();
        return new ActorDto(actor.Id, actor.ActorName, actor.ActorRole, actor.SortOrder);
    }

    [HttpDelete("{id:int}/actors/{actorId:int}")]
    public async Task<IActionResult> DeleteActor(int id, int actorId)
    {
        var actor = await _db.CaseStudyActors.FirstOrDefaultAsync(a => a.Id == actorId && a.CaseStudyId == id);
        if (actor is null) return NotFound();

        _db.CaseStudyActors.Remove(actor);
        await _db.SaveChangesAsync();
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

        await _db.CaseStudyMetrics.AddAsync(metric);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id }, new MetricDto(metric.Id, metric.MetricLabel, metric.MetricValue, metric.MetricUnit, metric.SortOrder));
    }

    [HttpPut("{id:int}/metrics/{metricId:int}")]
    public async Task<ActionResult<MetricDto>> UpdateMetric(int id, int metricId, MetricRequest req)
    {
        var metric = await _db.CaseStudyMetrics.FirstOrDefaultAsync(m => m.Id == metricId && m.CaseStudyId == id);
        if (metric is null) return NotFound();

        metric.MetricLabel = req.MetricLabel;
        metric.MetricValue = req.MetricValue;
        metric.MetricUnit = req.MetricUnit;
        metric.SortOrder = req.SortOrder;

        await _db.SaveChangesAsync();
        return new MetricDto(metric.Id, metric.MetricLabel, metric.MetricValue, metric.MetricUnit, metric.SortOrder);
    }

    [HttpDelete("{id:int}/metrics/{metricId:int}")]
    public async Task<IActionResult> DeleteMetric(int id, int metricId)
    {
        var metric = await _db.CaseStudyMetrics.FirstOrDefaultAsync(m => m.Id == metricId && m.CaseStudyId == id);
        if (metric is null) return NotFound();

        _db.CaseStudyMetrics.Remove(metric);
        await _db.SaveChangesAsync();
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

        await _db.CaseStudyEventFlowSteps.AddAsync(step);
        await _db.SaveChangesAsync();

        var actorName = req.StepActorId.HasValue
            ? (await _db.CaseStudyActors.FindAsync(req.StepActorId.Value))?.ActorName
            : null;

        return CreatedAtAction(nameof(GetById), new { id }, new EventFlowStepDto(step.Id, step.StepNumber, step.StepDescription, actorName));
    }

    [HttpPut("{id:int}/event-flow-steps/{stepId:int}")]
    public async Task<ActionResult<EventFlowStepDto>> UpdateEventFlowStep(int id, int stepId, EventFlowStepRequest req)
    {
        var step = await _db.CaseStudyEventFlowSteps
            .Include(s => s.StepActor)
            .FirstOrDefaultAsync(s => s.Id == stepId && s.CaseStudyId == id);
        if (step is null) return NotFound();

        step.StepNumber = req.StepNumber;
        step.StepDescription = req.StepDescription;
        step.StepActorId = req.StepActorId;

        await _db.SaveChangesAsync();
        return new EventFlowStepDto(step.Id, step.StepNumber, step.StepDescription, step.StepActor?.ActorName);
    }

    [HttpDelete("{id:int}/event-flow-steps/{stepId:int}")]
    public async Task<IActionResult> DeleteEventFlowStep(int id, int stepId)
    {
        var step = await _db.CaseStudyEventFlowSteps.FirstOrDefaultAsync(s => s.Id == stepId && s.CaseStudyId == id);
        if (step is null) return NotFound();

        _db.CaseStudyEventFlowSteps.Remove(step);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CaseStudySummaryDto ToSummaryDto(CaseStudy c) =>
        new(c.Id, c.DescriptiveName, c.Slug, c.Status, c.ExecutiveSummary, c.Subtitle, c.PrimaryActor, c.PublishedAt);

    private static CaseStudyDetailDto ToDetailDto(CaseStudy c) =>
        new(c.Id, c.DescriptiveName, c.Slug, c.Status,
            c.ExecutiveSummary, c.Subtitle, c.PrimaryActor, c.Trigger,
            c.ProblemChallenge, c.Solution, c.PostConditions, c.Exceptions, c.IndustryCitation,
            c.CreatedAt, c.UpdatedAt, c.PublishedAt,
            c.CaseStudyActors.OrderBy(a => a.SortOrder).Select(a => new ActorDto(a.Id, a.ActorName, a.ActorRole, a.SortOrder)).ToList(),
            c.CaseStudyMetrics.OrderBy(m => m.SortOrder).Select(m => new MetricDto(m.Id, m.MetricLabel, m.MetricValue, m.MetricUnit, m.SortOrder)).ToList(),
            c.CaseStudyEventFlowSteps.OrderBy(s => s.StepNumber).Select(s => new EventFlowStepDto(s.Id, s.StepNumber, s.StepDescription, s.StepActor?.ActorName)).ToList());
}
