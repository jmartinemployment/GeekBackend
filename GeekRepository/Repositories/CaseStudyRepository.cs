using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GeekApplication.Entities;
using GeekApplication.Interfaces;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Repositories;

public class CaseStudyRepository : ICaseStudyRepository
{
    private readonly AppDbContext _context;

    public CaseStudyRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CaseStudy?> GetByIdAsync(int id) =>
        await _context.CaseStudies
            .Include(c => c.CaseStudyActors.OrderBy(a => a.SortOrder))
            .Include(c => c.CaseStudyMetrics.OrderBy(m => m.SortOrder))
            .Include(c => c.CaseStudyEventFlowSteps.OrderBy(s => s.StepNumber))
                .ThenInclude(s => s.StepActor)
            .FirstOrDefaultAsync(c => c.Id == id);

    public async Task<CaseStudy?> GetBySlugAsync(string slug) =>
        await _context.CaseStudies
            .Include(c => c.CaseStudyActors.OrderBy(a => a.SortOrder))
            .Include(c => c.CaseStudyMetrics.OrderBy(m => m.SortOrder))
            .Include(c => c.CaseStudyEventFlowSteps.OrderBy(s => s.StepNumber))
                .ThenInclude(s => s.StepActor)
            .FirstOrDefaultAsync(c => c.Slug == slug);

    public async Task<IReadOnlyList<CaseStudy>> GetAllAsync() =>
        await _context.CaseStudies
            .OrderBy(c => c.DescriptiveName)
            .ToListAsync();

    public async Task<IReadOnlyList<CaseStudy>> GetPublishedAsync() =>
        await _context.CaseStudies
            .Where(c => c.Status == "published")
            .OrderByDescending(c => c.PublishedAt)
            .ToListAsync();

    public async Task AddAsync(CaseStudy caseStudy)
    {
        await _context.CaseStudies.AddAsync(caseStudy);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(CaseStudy caseStudy)
    {
        _context.CaseStudies.Update(caseStudy);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _context.CaseStudies.FindAsync(id);
        if (entity is not null)
        {
            _context.CaseStudies.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<CaseStudyActor?> GetActorAsync(int caseStudyId, int actorId) =>
        await _context.CaseStudyActors.FirstOrDefaultAsync(a => a.Id == actorId && a.CaseStudyId == caseStudyId);

    public async Task<CaseStudyActor> AddActorAsync(int caseStudyId, CaseStudyActor actor)
    {
        actor.CaseStudyId = caseStudyId;
        await _context.CaseStudyActors.AddAsync(actor);
        await _context.SaveChangesAsync();
        return actor;
    }

    public async Task<CaseStudyActor?> UpdateActorAsync(int caseStudyId, int actorId, string actorName, string actorRole, int sortOrder)
    {
        var actor = await _context.CaseStudyActors.FirstOrDefaultAsync(a => a.Id == actorId && a.CaseStudyId == caseStudyId);
        if (actor is null) return null;
        actor.ActorName = actorName;
        actor.ActorRole = actorRole;
        actor.SortOrder = sortOrder;
        await _context.SaveChangesAsync();
        return actor;
    }

    public async Task DeleteActorAsync(int caseStudyId, int actorId)
    {
        var actor = await _context.CaseStudyActors.FirstOrDefaultAsync(a => a.Id == actorId && a.CaseStudyId == caseStudyId);
        if (actor is not null)
        {
            _context.CaseStudyActors.Remove(actor);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<CaseStudyMetric?> GetMetricAsync(int caseStudyId, int metricId) =>
        await _context.CaseStudyMetrics.FirstOrDefaultAsync(m => m.Id == metricId && m.CaseStudyId == caseStudyId);

    public async Task<CaseStudyMetric> AddMetricAsync(int caseStudyId, CaseStudyMetric metric)
    {
        metric.CaseStudyId = caseStudyId;
        await _context.CaseStudyMetrics.AddAsync(metric);
        await _context.SaveChangesAsync();
        return metric;
    }

    public async Task<CaseStudyMetric?> UpdateMetricAsync(int caseStudyId, int metricId, string metricLabel, string metricValue, string? metricUnit, int sortOrder)
    {
        var metric = await _context.CaseStudyMetrics.FirstOrDefaultAsync(m => m.Id == metricId && m.CaseStudyId == caseStudyId);
        if (metric is null) return null;
        metric.MetricLabel = metricLabel;
        metric.MetricValue = metricValue;
        metric.MetricUnit = metricUnit;
        metric.SortOrder = sortOrder;
        await _context.SaveChangesAsync();
        return metric;
    }

    public async Task DeleteMetricAsync(int caseStudyId, int metricId)
    {
        var metric = await _context.CaseStudyMetrics.FirstOrDefaultAsync(m => m.Id == metricId && m.CaseStudyId == caseStudyId);
        if (metric is not null)
        {
            _context.CaseStudyMetrics.Remove(metric);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<CaseStudyEventFlowStep?> GetEventFlowStepAsync(int caseStudyId, int stepId) =>
        await _context.CaseStudyEventFlowSteps
            .Include(s => s.StepActor)
            .FirstOrDefaultAsync(s => s.Id == stepId && s.CaseStudyId == caseStudyId);

    public async Task<CaseStudyEventFlowStep> AddEventFlowStepAsync(int caseStudyId, CaseStudyEventFlowStep step)
    {
        step.CaseStudyId = caseStudyId;
        await _context.CaseStudyEventFlowSteps.AddAsync(step);
        await _context.SaveChangesAsync();
        await _context.Entry(step).Reference(s => s.StepActor).LoadAsync();
        return step;
    }

    public async Task<CaseStudyEventFlowStep?> UpdateEventFlowStepAsync(int caseStudyId, int stepId, int stepNumber, string stepDescription, int? stepActorId)
    {
        var step = await _context.CaseStudyEventFlowSteps
            .Include(s => s.StepActor)
            .FirstOrDefaultAsync(s => s.Id == stepId && s.CaseStudyId == caseStudyId);
        if (step is null) return null;
        step.StepNumber = stepNumber;
        step.StepDescription = stepDescription;
        step.StepActorId = stepActorId;
        await _context.SaveChangesAsync();
        await _context.Entry(step).Reference(s => s.StepActor).LoadAsync();
        return step;
    }

    public async Task DeleteEventFlowStepAsync(int caseStudyId, int stepId)
    {
        var step = await _context.CaseStudyEventFlowSteps.FirstOrDefaultAsync(s => s.Id == stepId && s.CaseStudyId == caseStudyId);
        if (step is not null)
        {
            _context.CaseStudyEventFlowSteps.Remove(step);
            await _context.SaveChangesAsync();
        }
    }
}
