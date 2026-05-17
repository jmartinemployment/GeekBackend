using System.Collections.Generic;
using System.Threading.Tasks;
using GeekApplication.Entities;

namespace GeekApplication.Interfaces;

public interface ICaseStudyRepository
{
    Task<CaseStudy?> GetByIdAsync(int id);
    Task<CaseStudy?> GetBySlugAsync(string slug);
    Task<IReadOnlyList<CaseStudy>> GetAllAsync();
    Task<IReadOnlyList<CaseStudy>> GetPublishedAsync();
    Task AddAsync(CaseStudy caseStudy);
    Task UpdateAsync(CaseStudy caseStudy);
    Task DeleteAsync(int id);

    Task<CaseStudyActor?> GetActorAsync(int caseStudyId, int actorId);
    Task<CaseStudyActor> AddActorAsync(int caseStudyId, CaseStudyActor actor);
    Task<CaseStudyActor?> UpdateActorAsync(int caseStudyId, int actorId, string actorName, string actorRole, int sortOrder);
    Task DeleteActorAsync(int caseStudyId, int actorId);

    Task<CaseStudyMetric?> GetMetricAsync(int caseStudyId, int metricId);
    Task<CaseStudyMetric> AddMetricAsync(int caseStudyId, CaseStudyMetric metric);
    Task<CaseStudyMetric?> UpdateMetricAsync(int caseStudyId, int metricId, string metricLabel, string metricValue, string? metricUnit, int sortOrder);
    Task DeleteMetricAsync(int caseStudyId, int metricId);

    Task<CaseStudyEventFlowStep?> GetEventFlowStepAsync(int caseStudyId, int stepId);
    Task<CaseStudyEventFlowStep> AddEventFlowStepAsync(int caseStudyId, CaseStudyEventFlowStep step);
    Task<CaseStudyEventFlowStep?> UpdateEventFlowStepAsync(int caseStudyId, int stepId, int stepNumber, string stepDescription, int? stepActorId);
    Task DeleteEventFlowStepAsync(int caseStudyId, int stepId);
}
