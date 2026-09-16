using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Infrastructure.InMemory;

namespace GeekAPI.Services.ContentCreatorV2.V1Restore;

/// <summary>
/// Materialises the v1 Project that a Create generates through.
///
/// v1's engine (ContentGenerationOrchestrator) is Project-keyed - GeneratePillarPlanAsync,
/// GenerateToolPagesAsync, GenerateAllAsync all take a projectId - so restoring v1 as the writer
/// means every Create needs a Project behind it. The link already exists in v1's model
/// (Project.LinkedCreateId plus a cached BriefJson); nothing in ContentCreatorV2 ever used it.
///
/// This class only maps what the Create genuinely provides. It does not invent hierarchy grounding:
/// HierarchyPath, HierarchyChildHeadings, HierarchyToolsByHeading and HierarchyAssignmentMarkdown are
/// left for the RAG grounding step to fill from verified retrieval. That matters, because v1 refuses
/// to generate when HierarchyPath is empty unless AllowOutsideSiteScope is set - which is the guard
/// that stops it writing "Introduction to ..." filler. Pre-filling those fields with anything other
/// than verified content would defeat the gate.
/// </summary>
public sealed class GccV2V1ProjectBridge(
    IProjectStore projects,
    IClientStore clients,
    HttpGccV2Repository repo,
    ILogger<GccV2V1ProjectBridge> logger)
{
    /// <summary>
    /// Returns the Project for this Create, creating it on first use. Idempotent: called twice for
    /// the same Create it updates the same Project rather than producing a second one.
    /// </summary>
    public async Task<Project> EnsureProjectForCreateAsync(Guid createId, CancellationToken ct)
    {
        var create = await repo.GetCreateAsync(createId, ct)
            ?? throw new InvalidOperationException($"Create {createId} not found; cannot build its v1 Project.");

        // Latest brief wins - briefs are versioned per create, and a frozen older version is history.
        var brief = (await repo.ListBriefsByCreateAsync(createId, ct))
            .OrderByDescending(b => b.Version)
            .ThenByDescending(b => b.CreatedAtUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Create {createId} has no brief. v1 refuses to generate without one "
                + "(GccGenerateService.ValidateBriefRequired), so there is nothing to build a Project from.");

        var client = (await clients.ListAsync(ct)).FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No v1 Client exists. Workflow startup seeds one; without it a Project has no owner.");

        var existing = (await projects.ListAsync(p => p.LinkedCreateId == createId, ct)).FirstOrDefault();
        var project = existing ?? new Project { ClientId = client.Id };

        // The operator's Title is the subject they asked for. v1 otherwise writes its own title from
        // TargetKeyword, which is how the title entered on the Create stopped reaching the output.
        var operatorTitle = (create.Title ?? "").Trim();
        var keyword = (brief.TargetKeyword ?? "").Trim();
        if (operatorTitle.Length == 0 && keyword.Length == 0)
            throw new InvalidOperationException(
                $"Create {createId} has neither a title nor a target keyword; v1 has no subject to write about.");

        project.Name = operatorTitle.Length > 0 ? operatorTitle : keyword;
        project.TargetKeyword = operatorTitle.Length > 0 ? operatorTitle : keyword;
        project.UseExactKeywordAsTitle = operatorTitle.Length > 0;

        project.ProjectUrl = (create.SiteUrl ?? "").Trim();
        project.SiteAnalysisId = create.ProjectSiteCrawlRunId;
        project.LinkedCreateId = createId;
        project.BriefJson = brief.RawBriefJson;
        project.UpdatedAtUtc = DateTime.UtcNow;

        if (existing is null)
        {
            await projects.AddAsync(project, ct);
            logger.LogInformation(
                "Created v1 Project {ProjectId} for Create {CreateId} (keyword '{Keyword}', exactTitle={Exact}).",
                project.Id, createId, project.TargetKeyword, project.UseExactKeywordAsTitle);
        }
        else
        {
            await projects.SaveAsync(project, ct);
            logger.LogInformation(
                "Updated v1 Project {ProjectId} from Create {CreateId}.", project.Id, createId);
        }

        return project;
    }
}
