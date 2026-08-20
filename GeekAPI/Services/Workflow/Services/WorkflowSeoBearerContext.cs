namespace GeekAPI.Services.Workflow.Services;

/// <summary>
/// Holds a bearer token for SEO calls when work runs outside the HTTP request
/// (e.g. tools generation jobs via Task.Run). Scoped per DI scope.
/// </summary>
public sealed class WorkflowSeoBearerContext
{
    public string? BearerToken { get; set; }
}
