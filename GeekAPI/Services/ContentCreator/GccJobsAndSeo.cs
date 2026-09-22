using System.Collections.Concurrent;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreator;

/// <summary>v1 in-memory job store. Site Analyzer client lives in <see cref="GeekAPI.Services.GeekSeo"/>.</summary>
public class GccJobStore
{
    private readonly ConcurrentDictionary<Guid, GccJob> _jobs = new();

    public GccJob Create(string kind, Guid createId, string ownerUserId)
    {
        var job = new GccJob(
            Guid.NewGuid(), kind, createId, ownerUserId, "running", null, null, DateTime.UtcNow, null);
        _jobs[job.Id] = job;
        return job;
    }

    public void Complete(Guid id, object? result) =>
        Update(id, j => j with { Status = "ready", ResultJson = JsonSerializer.Serialize(result), CompletedAtUtc = DateTime.UtcNow });

    public void Fail(Guid id, string error) =>
        Update(id, j => j with { Status = "failed", Error = error, CompletedAtUtc = DateTime.UtcNow });

    public GccJob? Get(Guid id) => _jobs.TryGetValue(id, out var j) ? j : null;

    private void Update(Guid id, Func<GccJob, GccJob> mutator)
    {
        _jobs.AddOrUpdate(id, _ => throw new KeyNotFoundException(), (_, cur) => mutator(cur));
    }
}

public sealed record GccJob(
    Guid Id,
    string Kind,
    Guid CreateId,
    /// <summary>Token subject that started this job. The hub authorises a join against this rather
    /// than re-reading the create, so joining costs no repository round trip.</summary>
    string OwnerUserId,
    string Status,
    string? ResultJson,
    string? Error,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);
