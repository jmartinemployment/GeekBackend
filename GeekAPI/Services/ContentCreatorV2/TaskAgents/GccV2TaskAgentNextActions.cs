using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>Maps compatibility artifact types onto runnable task-agent capabilities.</summary>
public static class GccV2TaskAgentNextActions
{
    private static readonly IReadOnlyDictionary<string, (string CapabilityId, string Label)> ByArtifact =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["readinessScore.v1"] = ("ai-readiness", "AI Readiness Score"),
            ["factDensityReport.v1"] = ("fact-density", "Fact Density Audit"),
            ["entityMap.v1"] = ("entity-mapper", "Entity Mapper"),
            ["schemaMarkup.v1"] = ("schema-markup", "Schema Markup"),
            ["queryPlan.v1"] = ("query-planner", "Query Planner"),
            ["readinessComparison.v1"] = ("ai-readiness-comparison", "AI Readiness Comparison"),
            ["contentGapAnalysis.v1"] = ("content-gap", "Content Gap Finder"),
            ["competitorAudit.v1"] = ("competitor-audit", "Competitor Audit"),
            ["competitorPositioning.v1"] = ("competitor-positioning", "Competitor Positioning"),
            ["competitorPageAnalysis.v1"] = ("competitor-page", "Competitor Page Analysis"),
            ["claimLedger.v1"] = ("citable-claims", "Citable Claims"),
            ["faqSet.v1"] = ("faq-generator", "FAQ Generator"),
            ["comparisonBrief.v1"] = ("comparison-brief", "Comparison Brief"),
            ["pillarOutline.v1"] = ("pillar-outline", "Pillar Article Outline"),
            ["competitiveResponse.v1"] = ("competitive-response", "Competitive Response"),
        };

    public static IReadOnlyList<object> FromCompatibilityJson(string compatibilityJson)
    {
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(compatibilityJson) ? "{}" : compatibilityJson);
            if (!document.RootElement.TryGetProperty("downstream", out var downstream)
                || downstream.ValueKind != JsonValueKind.Array)
                return [];
            var actions = new List<object>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in downstream.EnumerateArray())
            {
                var artifactType = entry.ValueKind == JsonValueKind.String ? entry.GetString() : null;
                if (string.IsNullOrWhiteSpace(artifactType)) continue;
                if (!ByArtifact.TryGetValue(artifactType, out var mapped)) continue;
                if (!seen.Add(mapped.CapabilityId)) continue;
                actions.Add(new
                {
                    capabilityId = mapped.CapabilityId,
                    label = mapped.Label,
                    artifactType,
                    relationship = "derived-from",
                });
            }
            return actions;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
