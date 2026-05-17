using GeekApplication.Entities;
using GeekApplication.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Content;

[ApiController]
[Route("repo/content/case-studies")]
public class CaseStudiesController : ControllerBase
{
    private readonly ICaseStudyRepository _repo;

    public CaseStudiesController(ICaseStudyRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<IReadOnlyList<CaseStudy>> GetPublished() => await _repo.GetPublishedAsync();

    [HttpGet("all")]
    public async Task<IReadOnlyList<CaseStudy>> GetAll() => await _repo.GetAllAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CaseStudy>> GetById(int id)
    {
        var c = await _repo.GetByIdAsync(id);
        return c is null ? NotFound() : Ok(c);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<CaseStudy>> GetBySlug(string slug)
    {
        var c = await _repo.GetBySlugAsync(slug);
        return c is null ? NotFound() : Ok(c);
    }

    [HttpPost]
    public async Task<ActionResult<CaseStudy>> Create([FromBody] CaseStudy caseStudy)
    {
        caseStudy.CreatedAt = DateTime.UtcNow;
        caseStudy.UpdatedAt = DateTime.UtcNow;
        if (caseStudy.Status == "published")
            caseStudy.PublishedAt = DateTime.UtcNow;
        await _repo.AddAsync(caseStudy);
        return CreatedAtAction(nameof(GetById), new { id = caseStudy.Id }, caseStudy);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CaseStudy>> Update(int id, [FromBody] CaseStudy caseStudy)
    {
        var existing = await _repo.GetByIdAsync(id);
        if (existing is null) return NotFound();
        var wasPublished = existing.Status == "published";
        caseStudy.Id = id;
        caseStudy.UpdatedAt = DateTime.UtcNow;
        if (!wasPublished && caseStudy.Status == "published")
            caseStudy.PublishedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(caseStudy);
        return Ok(caseStudy);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        if (existing is null) return NotFound();
        await _repo.DeleteAsync(id);
        return NoContent();
    }

    // ── Actors ───────────────────────────────────────────────────────────────

    [HttpPost("{id:int}/actors")]
    public async Task<ActionResult<CaseStudyActor>> AddActor(int id, [FromBody] CaseStudyActor actor)
    {
        var c = await _repo.GetByIdAsync(id);
        if (c is null) return NotFound();
        var result = await _repo.AddActorAsync(id, actor);
        return CreatedAtAction(nameof(GetById), new { id }, result);
    }

    [HttpPut("{id:int}/actors/{actorId:int}")]
    public async Task<ActionResult<CaseStudyActor>> UpdateActor(int id, int actorId, [FromBody] CaseStudyActor actor)
    {
        var result = await _repo.UpdateActorAsync(id, actorId, actor.ActorName, actor.ActorRole, actor.SortOrder);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:int}/actors/{actorId:int}")]
    public async Task<IActionResult> DeleteActor(int id, int actorId)
    {
        var existing = await _repo.GetActorAsync(id, actorId);
        if (existing is null) return NotFound();
        await _repo.DeleteActorAsync(id, actorId);
        return NoContent();
    }

    // ── Metrics ──────────────────────────────────────────────────────────────

    [HttpPost("{id:int}/metrics")]
    public async Task<ActionResult<CaseStudyMetric>> AddMetric(int id, [FromBody] CaseStudyMetric metric)
    {
        var c = await _repo.GetByIdAsync(id);
        if (c is null) return NotFound();
        var result = await _repo.AddMetricAsync(id, metric);
        return CreatedAtAction(nameof(GetById), new { id }, result);
    }

    [HttpPut("{id:int}/metrics/{metricId:int}")]
    public async Task<ActionResult<CaseStudyMetric>> UpdateMetric(int id, int metricId, [FromBody] CaseStudyMetric metric)
    {
        var result = await _repo.UpdateMetricAsync(id, metricId, metric.MetricLabel, metric.MetricValue, metric.MetricUnit, metric.SortOrder);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:int}/metrics/{metricId:int}")]
    public async Task<IActionResult> DeleteMetric(int id, int metricId)
    {
        var existing = await _repo.GetMetricAsync(id, metricId);
        if (existing is null) return NotFound();
        await _repo.DeleteMetricAsync(id, metricId);
        return NoContent();
    }

    // ── Event Flow Steps ─────────────────────────────────────────────────────

    [HttpPost("{id:int}/event-flow-steps")]
    public async Task<ActionResult<CaseStudyEventFlowStep>> AddEventFlowStep(int id, [FromBody] CaseStudyEventFlowStep step)
    {
        var c = await _repo.GetByIdAsync(id);
        if (c is null) return NotFound();
        var result = await _repo.AddEventFlowStepAsync(id, step);
        return CreatedAtAction(nameof(GetById), new { id }, result);
    }

    [HttpPut("{id:int}/event-flow-steps/{stepId:int}")]
    public async Task<ActionResult<CaseStudyEventFlowStep>> UpdateEventFlowStep(int id, int stepId, [FromBody] CaseStudyEventFlowStep step)
    {
        var result = await _repo.UpdateEventFlowStepAsync(id, stepId, step.StepNumber, step.StepDescription, step.StepActorId);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:int}/event-flow-steps/{stepId:int}")]
    public async Task<IActionResult> DeleteEventFlowStep(int id, int stepId)
    {
        var existing = await _repo.GetEventFlowStepAsync(id, stepId);
        if (existing is null) return NotFound();
        await _repo.DeleteEventFlowStepAsync(id, stepId);
        return NoContent();
    }
}
