using System.Collections.Concurrent;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreator;

/// <summary>v1 in-memory job store.</summary>
public class GccJobStore
{
    private readonly ConcurrentDictionary<Guid, GccJob> _jobs = new();

    /// <summary>
    /// How long a finished job stays readable. The terminal hub event carries the result, and nothing
    /// reads a job back afterwards except a late JoinGccGenerate from a reconnecting client, which
    /// happens within minutes, not hours.
    /// </summary>
    private static readonly TimeSpan FinishedJobRetention = TimeSpan.FromHours(6);

    public GccJob Create(string kind, Guid createId, string ownerUserId)
    {
        EvictFinished();
        var job = new GccJob(
            Guid.NewGuid(), kind, createId, ownerUserId, "running", null, null, DateTime.UtcNow, null);
        _jobs[job.Id] = job;
        return job;
    }

    /// <summary>
    /// Finished jobs older than <see cref="FinishedJobRetention"/> are dropped. Complete() stores the
    /// whole generate result -- every artifact body -- as ResultJson, and this dictionary is a
    /// singleton that never evicted, so the process held every page it had ever generated until a
    /// redeploy. Running jobs are never touched.
    /// </summary>
    private void EvictFinished()
    {
        var cutoff = DateTime.UtcNow - FinishedJobRetention;
        foreach (var (id, job) in _jobs)
        {
            if (job.CompletedAtUtc is { } completed && completed < cutoff)
                _jobs.TryRemove(id, out _);
        }
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
