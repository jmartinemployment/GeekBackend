using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Workflow;

[ApiController]
[Route("api/projects/{projectId:guid}/generate")]
public class GenerateController : ControllerBase
{
    private readonly IContentGenerationOrchestrator _orchestrator;
    private readonly ToolsGenerationJobRunner _toolsJobs;
    private readonly ToolsGenerationJobStore _toolsJobStore;
    private readonly ILogger<GenerateController> _logger;

    public GenerateController(
        IContentGenerationOrchestrator orchestrator,
        ToolsGenerationJobRunner toolsJobs,
        ToolsGenerationJobStore toolsJobStore,
        ILogger<GenerateController> logger)
    {
        _orchestrator = orchestrator;
        _toolsJobs = toolsJobs;
        _toolsJobStore = toolsJobStore;
        _logger = logger;
    }

    [HttpPost("pillar/plan")]
    public Task<IActionResult> GeneratePillarPlan(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GeneratePillarPlanAsync(projectId, cancellationToken), "pillar-plan", cancellationToken);

    [HttpPost("pillar/body")]
    public Task<IActionResult> GeneratePillarBody(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GeneratePillarBodyAsync(projectId, cancellationToken: cancellationToken), "pillar-body", cancellationToken);

    [HttpPost("pillar")]
    public Task<IActionResult> GeneratePillar(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GeneratePillarAsync(projectId, cancellationToken), "pillar", cancellationToken);

    /// <summary>Starts crawl tools generation in the background. Poll GET tools/jobs/{jobId}.</summary>
    [HttpPost("tools")]
    public IActionResult GenerateToolPages(Guid projectId)
    {
        var job = _toolsJobs.StartCrawlTools(projectId);
        return Accepted(ToResponse(job));
    }

    /// <summary>Starts names-only tools generation in the background. Poll GET tools/jobs/{jobId}.</summary>
    [HttpPost("tools-from-names")]
    public IActionResult GenerateToolPagesFromNames(
        Guid projectId,
        [FromBody] GenerateToolsFromNamesRequest? request)
    {
        var names = (request?.ToolNames ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            return BadRequest(new { error = "toolNames required (non-empty after trim)." });
        }

        var job = _toolsJobs.StartToolsFromNames(projectId, names, request?.Brief);
        return Accepted(ToResponse(job));
    }

    [HttpGet("tools/jobs/{jobId:guid}")]
    public IActionResult GetToolsJob(Guid projectId, Guid jobId)
    {
        var job = _toolsJobStore.Get(jobId);
        if (job is null || job.ProjectId != projectId)
            return NotFound();
        return Ok(ToResponse(job));
    }

    [HttpPost("blog")]
    public Task<IActionResult> GenerateBlog(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GenerateBlogAsync(projectId, cancellationToken: cancellationToken), "blog", cancellationToken);

    [HttpPost("social")]
    public Task<IActionResult> GenerateSocial(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GenerateSocialAsync(projectId, cancellationToken), "social", cancellationToken);

    [HttpPost("email-cold-outreach")]
    public Task<IActionResult> GenerateColdOutreach(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GenerateColdOutreachAsync(projectId, cancellationToken), "email-cold-outreach", cancellationToken);

    [HttpPost("image-prompts")]
    public Task<IActionResult> GenerateImagePrompts(
        Guid projectId, [FromBody] GenerateImagePromptsRequest? request, CancellationToken cancellationToken)
    {
        var headings = request?.SectionHeadingsToTest is { Count: > 0 } h ? new HashSet<string>(h, StringComparer.OrdinalIgnoreCase) : null;
        return RunStep(
            projectId,
            _orchestrator.GenerateImagePromptsAsync(projectId, headings, cancellationToken),
            "image-prompts",
            cancellationToken);
    }

    [HttpPost]
    public Task<IActionResult> GenerateAll(Guid projectId, CancellationToken cancellationToken) =>
        RunStep(projectId, _orchestrator.GenerateAllAsync(projectId, cancellationToken), "all", cancellationToken);

    private static ToolsGenerationJobResponse ToResponse(ToolsGenerationJob job) =>
        new(job.Id, job.ProjectId, job.Kind, job.Status, job.Completed, job.Total, job.Error, job.Result);

    private async Task<IActionResult> RunStep(
        Guid projectId, Task<GeneratedContentSet> action, string step, CancellationToken cancellationToken)
    {
        try
        {
            var result = await action;
            return Ok(result);
        }
        catch (ContentGenerationException ex)
        {
            _logger.LogWarning(ex, "Content generation step {Step} failed for project {ProjectId}", step, projectId);
            var title = ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                ? "Generation timed out"
                : "Content generation failed";
            return Problem(ex.Message, statusCode: 502, title: title);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Content generation step {Step} timed out for project {ProjectId}", step, projectId);
            return Problem(
                "The LLM provider did not respond in time. Try again, or split the step into smaller pieces.",
                statusCode: 504,
                title: "Generation timed out");
        }
    }
}

/// <summary>When SectionHeadingsToTest is empty/omitted, every section is (re)generated, same as before.
/// When populated, only the listed section headings (pillar/blog H2 text, or the pillar/blog title for the
/// hero images, or a tool name) are regenerated — existing image prompts for other sections are left as-is.</summary>
public sealed record GenerateImagePromptsRequest(List<string>? SectionHeadingsToTest = null);

public sealed record GenerateToolsFromNamesRequest(List<string>? ToolNames = null, string? Brief = null);
