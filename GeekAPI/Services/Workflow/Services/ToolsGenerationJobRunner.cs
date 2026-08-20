using GeekAPI.Services.Workflow.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>
/// Starts tools generation on a background Task with its own DI scope so the HTTP request can return 202.
/// Captures the request bearer so SEO tree calls still authenticate after HttpContext is gone.
/// </summary>
public sealed class ToolsGenerationJobRunner
{
    private readonly ToolsGenerationJobStore _jobs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ToolsGenerationJobRunner> _logger;

    public ToolsGenerationJobRunner(
        ToolsGenerationJobStore jobs,
        IServiceScopeFactory scopeFactory,
        ILogger<ToolsGenerationJobRunner> logger)
    {
        _jobs = jobs;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public ToolsGenerationJob StartCrawlTools(Guid projectId, string? bearerToken)
    {
        // Total unknown until crawl resolve; UI shows indeterminate until SetTotal.
        var job = _jobs.Create(projectId, "tools", total: 0);
        _ = Task.Run(() => RunCrawlAsync(job.Id, projectId, bearerToken));
        return job;
    }

    public ToolsGenerationJob StartToolsFromNames(
        Guid projectId, IReadOnlyList<string> toolNames, string? brief, string? bearerToken)
    {
        var names = toolNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Product pages + hub.
        var job = _jobs.Create(projectId, "tools-from-names", total: names.Count + 1);
        _ = Task.Run(() => RunFromNamesAsync(job.Id, projectId, names, brief, bearerToken));
        return job;
    }

    private async Task RunCrawlAsync(Guid jobId, Guid projectId, string? bearerToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<WorkflowSeoBearerContext>().BearerToken = bearerToken;
            // #region agent log
            _logger.LogInformation(
                "Tools crawl job {JobId} starting with bearerPresent={BearerPresent} for project {ProjectId}",
                jobId,
                !string.IsNullOrWhiteSpace(bearerToken),
                projectId);
            // #endregion
            var orchestrator = scope.ServiceProvider.GetRequiredService<IContentGenerationOrchestrator>();
            var result = await orchestrator.GenerateToolPagesAsync(
                projectId,
                revisionNotes: null,
                toolSlugsToRegenerate: null,
                reportProgress: (completed, total) =>
                {
                    if (total > 0) _jobs.SetTotal(jobId, total);
                    _jobs.SetProgress(jobId, completed);
                },
                cancellationToken: CancellationToken.None);
            _jobs.Complete(jobId, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tools generation job {JobId} failed for project {ProjectId}", jobId, projectId);
            _jobs.Fail(jobId, ex is ContentGenerationException cg ? cg.Message : ex.Message);
        }
    }

    private async Task RunFromNamesAsync(
        Guid jobId, Guid projectId, IReadOnlyList<string> names, string? brief, string? bearerToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<WorkflowSeoBearerContext>().BearerToken = bearerToken;
            var orchestrator = scope.ServiceProvider.GetRequiredService<IContentGenerationOrchestrator>();
            var result = await orchestrator.GenerateToolPagesFromNamesAsync(
                projectId,
                names,
                brief,
                reportProgress: (completed, total) =>
                {
                    if (total > 0) _jobs.SetTotal(jobId, total);
                    _jobs.SetProgress(jobId, completed);
                },
                cancellationToken: CancellationToken.None);
            _jobs.Complete(jobId, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tools-from-names job {JobId} failed for project {ProjectId}", jobId, projectId);
            _jobs.Fail(jobId, ex is ContentGenerationException cg ? cg.Message : ex.Message);
        }
    }
}
