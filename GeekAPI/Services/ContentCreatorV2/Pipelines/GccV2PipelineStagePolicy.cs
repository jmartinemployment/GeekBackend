using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Pipelines;

/// <summary>Validates Geek Content Pipeline stage DAGs and digests.</summary>
public static class GccV2PipelineStagePolicy
{
    public static readonly string[] LifecycleStages =
        ["plan", "create", "adapt", "activate", "optimize"];

    private static readonly HashSet<string> Lifecycles = new(LifecycleStages, StringComparer.Ordinal);
    private static readonly HashSet<string> Kinds = new(
        ["task-agent", "handoff", "approval"], StringComparer.Ordinal);

    public static string? ValidateStagesJson(string stagesJson, out IReadOnlyList<PipelineStage> stages)
    {
        stages = [];
        if (string.IsNullOrWhiteSpace(stagesJson))
            return "Pipeline stages are required.";
        try
        {
            var parsed = JsonSerializer.Deserialize<List<PipelineStage>>(stagesJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
            if (parsed.Count == 0)
                return "Pipeline must define at least one stage.";
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stage in parsed)
            {
                if (string.IsNullOrWhiteSpace(stage.Key))
                    return "Each pipeline stage needs a key.";
                if (!keys.Add(stage.Key.Trim()))
                    return $"Duplicate pipeline stage key: {stage.Key}.";
                if (!Lifecycles.Contains(stage.Lifecycle))
                    return $"Unknown lifecycle stage '{stage.Lifecycle}'.";
                if (!Kinds.Contains(stage.Kind))
                    return $"Unknown stage kind '{stage.Kind}'.";
                if (string.IsNullOrWhiteSpace(stage.DisplayName))
                    return $"Stage '{stage.Key}' needs a displayName.";
                if (stage.Kind == "task-agent" && string.IsNullOrWhiteSpace(stage.CapabilityId))
                    return $"Task-agent stage '{stage.Key}' needs a capabilityId.";
                if (stage.Kind == "handoff" && string.IsNullOrWhiteSpace(stage.Handoff))
                    return $"Handoff stage '{stage.Key}' needs a handoff target.";
            }
            foreach (var stage in parsed)
            {
                foreach (var dep in stage.DependsOn ?? [])
                {
                    if (!keys.Contains(dep))
                        return $"Stage '{stage.Key}' depends on missing stage '{dep}'.";
                }
            }
            if (!LifecycleStages.All(lifecycle =>
                    parsed.Any(stage => string.Equals(stage.Lifecycle, lifecycle, StringComparison.Ordinal))))
            {
                return "Pipeline template must include all five lifecycle stages: plan, create, adapt, activate, optimize.";
            }
            stages = parsed;
            return null;
        }
        catch (JsonException)
        {
            return "Pipeline stages must be a JSON array.";
        }
    }

    public static string Digest(string stagesJson, string policyJson)
    {
        var canonical = $"{stagesJson.Trim()}\n{(string.IsNullOrWhiteSpace(policyJson) ? "{}" : policyJson.Trim())}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string DefaultAeoTemplateStagesJson() =>
        """
        [
          {"key":"plan-queries","lifecycle":"plan","kind":"task-agent","capabilityId":"query-planner","displayName":"Query Planner","dependsOn":[]},
          {"key":"create-faq","lifecycle":"create","kind":"task-agent","capabilityId":"faq-generator","displayName":"FAQ Generator","dependsOn":["plan-queries"]},
          {"key":"adapt-canvas","lifecycle":"adapt","kind":"handoff","handoff":"canvas","displayName":"Canvas adapt","dependsOn":["create-faq"]},
          {"key":"activate-publish","lifecycle":"activate","kind":"handoff","handoff":"publish","displayName":"Publish handoff","dependsOn":["adapt-canvas"]},
          {"key":"optimize-readiness","lifecycle":"optimize","kind":"task-agent","capabilityId":"ai-readiness","displayName":"AI Readiness Score","dependsOn":["activate-publish"]}
        ]
        """;

    public sealed record PipelineStage(
        string Key,
        string Lifecycle,
        string Kind,
        string DisplayName,
        string? CapabilityId = null,
        string? Handoff = null,
        IReadOnlyList<string>? DependsOn = null);
}
