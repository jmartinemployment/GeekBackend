using System.Text.Json;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed record GccV2JobModelPolicyOverride(
    string Version,
    string Preset,
    IReadOnlyDictionary<string, string> StageModels,
    bool DowngradeConfirmed,
    string Reason,
    string? OperatorNote,
    string OperatorUserId,
    DateTimeOffset Timestamp,
    string? ReplacedAttemptId,
    string AttemptId,
    Guid JobId);

public sealed class GccV2JobModelPolicyOverrideStore
{
    public const string StageName = "model-policy-override";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpGccV2Repository _repo;

    public GccV2JobModelPolicyOverrideStore(HttpGccV2Repository repo) => _repo = repo;

    public async Task<GccV2JobModelPolicyOverride?> LoadLatestAsync(Guid jobId, CancellationToken ct)
    {
        var results = await _repo.GetStageResultsAsync(jobId, ct);
        foreach (var result in results
                     .Where(r => string.Equals(r.Stage, StageName, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(r => r.CompletedAtUtc))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<GccV2JobModelPolicyOverride>(result.OutputJson, JsonOpts);
                if (parsed is not null) return parsed;
            }
            catch (JsonException)
            {
                // Ignore malformed historical entries and continue to the previous audited record.
            }
        }
        return null;
    }

    public async Task<GccV2JobModelPolicyOverride> AppendAsync(
        Guid jobId,
        string producerStage,
        string model,
        string reason,
        string? operatorNote,
        string operatorUserId,
        string? replacedAttemptId,
        CancellationToken ct)
    {
        var current = await LoadLatestAsync(jobId, ct);
        var stageModels = new Dictionary<string, string>(
            current?.StageModels ?? new Dictionary<string, string>(),
            StringComparer.Ordinal);
        foreach (var approved in ContentModelPolicy.ApprovedStageModels)
            stageModels.TryAdd(approved.Key, approved.Value[0]);
        stageModels[producerStage] = model;

        var entry = new GccV2JobModelPolicyOverride(
            ContentModelPolicy.CurrentVersion,
            "custom",
            stageModels,
            true,
            reason,
            operatorNote,
            operatorUserId,
            DateTimeOffset.UtcNow,
            replacedAttemptId,
            Guid.NewGuid().ToString("D"),
            jobId);
        await _repo.AddStageResultAsync(
            jobId,
            new CreateGccV2StageResultCommand(
                StageName,
                producerStage,
                JsonSerializer.Serialize(entry, JsonOpts),
                0),
            ct);
        return entry;
    }
}
