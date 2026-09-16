using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Plan;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreatorV2.V1Restore;

/// <summary>
/// PLAN, written by v1.
///
/// This is the actual replacement, not a bridge to one: the job worker calls this instead of
/// GccV2PlanService.BuildOutlineAsync, so the outline a Create shows is produced by
/// ContentGenerationOrchestrator.GeneratePillarPlanAsync - v1's writer, with v1's guards
/// (BuildConsultantAppendix's ban on AI filler, the refusal to generate outside site scope,
/// UseExactKeywordAsTitle) rather than V2's.
///
/// Shape reconciliation: v1 returns ArticleDraft.SectionOutline, a flat list of headings. V2's
/// outline sections additionally carry a Job role. v1 does not assign roles, and
/// GccV2OutlinePutValidator passes an outline through unchanged when no section declares one
/// ("legacy outlines pass through"), so the headings are carried across without inventing roles
/// for them. Inventing a role here would be a fabricated structure, which is the failure mode this
/// whole restore exists to remove.
/// </summary>
public sealed class GccV2V1PlanAdapter(
    GccV2V1ProjectBridge bridge,
    ContentGenerationOrchestrator orchestrator,
    ILogger<GccV2V1PlanAdapter> logger)
{
    public async Task<GccV2PlanOutline> BuildOutlineAsync(
        GccV2JobDto job,
        GccV2BriefDto brief,
        CancellationToken ct)
    {
        var project = await bridge.EnsureProjectForCreateAsync(job.CreateId, ct);

        logger.LogInformation(
            "PLAN via v1 for job {JobId}: project {ProjectId}, keyword '{Keyword}'.",
            job.Id, project.Id, project.TargetKeyword);

        var generated = await orchestrator.GeneratePillarPlanAsync(project.Id, ct);

        var article = generated.Article
            ?? throw new InvalidOperationException(
                $"v1 PLAN produced no article for project {project.Id}. No outline to persist.");

        var headings = article.SectionOutline
            .Select(h => (h ?? "").Trim())
            .Where(h => h.Length > 0)
            .ToList();

        if (headings.Count == 0)
            throw new InvalidOperationException(
                $"v1 PLAN returned an article with no section outline for project {project.Id}.");

        var sections = headings
            .Select((heading, index) => new GccV2PlanOutlineSection(
                Key: $"s{index + 1}",
                Heading: heading,
                Job: string.Empty,
                HierarchyChildHeadings: [],
                Brief: null,
                EvidenceIds: null))
            .ToList();

        logger.LogInformation(
            "PLAN via v1 for job {JobId}: '{Title}', {SectionCount} sections.",
            job.Id, article.Title, sections.Count);

        return new GccV2PlanOutline(sections, project.HierarchyChildHeadings.ToList());
    }
}
