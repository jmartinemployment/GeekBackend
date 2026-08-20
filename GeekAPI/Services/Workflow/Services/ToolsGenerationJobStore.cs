using System.Collections.Concurrent;
using GeekAPI.Services.Workflow.DTOs;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>
/// In-process tools-generation jobs (same shape as GccJobStore). One GeekAPI instance only.
/// </summary>
public sealed class ToolsGenerationJobStore
{
    private readonly ConcurrentDictionary<Guid, ToolsGenerationJob> _jobs = new();

    public ToolsGenerationJob Create(Guid projectId, string kind, int total)
    {
        var job = new ToolsGenerationJob(
            Id: Guid.NewGuid(),
            ProjectId: projectId,
            Kind: kind,
            Status: "running",
            Completed: 0,
            Total: Math.Max(total, 0),
            Error: null,
            Result: null,
            CreatedAtUtc: DateTime.UtcNow,
            CompletedAtUtc: null);
        _jobs[job.Id] = job;
        return job;
    }

    public ToolsGenerationJob? Get(Guid id) =>
        _jobs.TryGetValue(id, out var job) ? job : null;

    public void SetTotal(Guid id, int total) =>
        Update(id, j => j with { Total = Math.Max(total, 0) });

    public void SetProgress(Guid id, int completed) =>
        Update(id, j => j with { Completed = Math.Max(completed, 0) });

    public void Complete(Guid id, GeneratedContentSet result) =>
        Update(id, j => j with
        {
            Status = "ready",
            Completed = j.Total > 0 ? j.Total : j.Completed,
            Result = result,
            CompletedAtUtc = DateTime.UtcNow,
        });

    public void Fail(Guid id, string error) =>
        Update(id, j => j with
        {
            Status = "failed",
            Error = error,
            CompletedAtUtc = DateTime.UtcNow,
        });

    private void Update(Guid id, Func<ToolsGenerationJob, ToolsGenerationJob> mutator)
    {
        _jobs.AddOrUpdate(id, _ => throw new KeyNotFoundException(), (_, cur) => mutator(cur));
    }
}

public sealed record ToolsGenerationJob(
    Guid Id,
    Guid ProjectId,
    string Kind,
    string Status,
    int Completed,
    int Total,
    string? Error,
    GeneratedContentSet? Result,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record ToolsGenerationJobResponse(
    Guid JobId,
    Guid ProjectId,
    string Kind,
    string Status,
    int Completed,
    int Total,
    string? Error,
    GeneratedContentSet? ContentSet);
