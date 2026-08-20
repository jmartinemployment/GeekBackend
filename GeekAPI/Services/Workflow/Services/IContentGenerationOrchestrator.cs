using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.Workflow.Services;

public interface IContentGenerationOrchestrator
{
    Task<GeneratedContentSet> GeneratePillarPlanAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GeneratePillarBodyAsync(Guid projectId, string? revisionNotes = null, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GeneratePillarAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateToolPagesAsync(
        Guid projectId,
        string? revisionNotes = null,
        IReadOnlySet<string>? toolSlugsToRegenerate = null,
        Action<int, int>? reportProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Writes ToolPost rows from a supplied name list. Does not use crawl slots or require a pillar.</summary>
    Task<GeneratedContentSet> GenerateToolPagesFromNamesAsync(
        Guid projectId,
        IReadOnlyList<string> toolNames,
        string? brief = null,
        Action<int, int>? reportProgress = null,
        CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateBlogAsync(Guid projectId, string? revisionNotes = null, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateSocialAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateColdOutreachAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateImagePromptsAsync(
        Guid projectId, IReadOnlySet<string>? sectionHeadingsToTest = null, CancellationToken cancellationToken = default);

    Task<GeneratedContentSet> GenerateAllAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Flattens a Project's crawled/keyword-source data into the shape prompt builders and the reviewer consume.</summary>
    ProjectGenerationContext BuildContext(Project project);
}
