using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Generation;

namespace GeekAPI.Services.ContentCreatorV2.AgentTests;

public static class GccV2AgentTestPolicy
{
    private static readonly HashSet<string> Roles = ["contributor", "producer", "reviewer"];
    private static readonly HashSet<string> Tools =
    [
        "search_corpus", "load_evidence_page", "get_brief_context", "get_outline_context",
        "get_completed_section_summaries", "get_specialist_artifacts", "activate_skill",
        "read_skill_resource", "submit_contribution", "submit_review", "submit_research_plan",
        "submit_outline", "submit_section", "submit_repair", "submit_validation",
        "submit_final_synthesis",
    ];
    private static readonly HashSet<string> ProducerStages =
        ["researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis", "complete"];

    public static IReadOnlyList<string> Failures(GccV2AgentVersionDto version)
    {
        var failures = new List<string>();
        var contentTypes = Strings(version.ContentTypesJson);
        var tools = Strings(version.AllowedToolsJson);
        var models = Strings(version.AllowedModelsJson);
        if (version.State is not ("approved" or "published" or "deprecated"))
            failures.Add("Version is not approved or published.");
        if (version.VersionDigest.Length != 64) failures.Add("Version digest is invalid.");
        if (string.IsNullOrWhiteSpace(version.Objective)) failures.Add("Objective is missing.");
        if (version.ModelPolicyVersion != ContentModelPolicy.CurrentVersion)
            failures.Add("Model policy version is not current.");
        if (string.IsNullOrWhiteSpace(version.ModelPolicyProfile))
            failures.Add("Model policy profile is missing.");
        if (version.Skills.Count == 0) failures.Add("No exact skill versions are assigned.");
        if (version.Skills.Any(x => x.SkillVersion.State != "published"))
            failures.Add("An assigned skill version is not published.");
        if (contentTypes.Count == 0) failures.Add("No content types are configured.");
        if (models.Count == 0) failures.Add("No models are configured.");
        if (version.StageParticipation.Count == 0) failures.Add("No stage participation is configured.");
        if (version.StageParticipation.Any(x => !Roles.Contains(x.Role)))
            failures.Add("An invalid specialist role is configured.");
        if (tools.Except(Tools, StringComparer.Ordinal).Any())
            failures.Add("A prohibited tool is configured.");
        var producerStages = version.StageParticipation.Where(x => x.Role == "producer")
            .Select(x => x.Stage).ToHashSet(StringComparer.Ordinal);
        if (producerStages.Count > 0 && !producerStages.SetEquals(ProducerStages))
            failures.Add("Producer does not cover every durable generation stage.");
        foreach (var participation in version.StageParticipation)
        {
            if (!models.Any(x => ContentModelPolicy.IsApproved(participation.Stage, x)))
                failures.Add($"No approved model intersection for {participation.Stage}.");
            foreach (var requiredTool in RuntimeTools(participation.Stage, participation.Role))
                if (!tools.Contains(requiredTool, StringComparer.Ordinal))
                    failures.Add($"Runtime tool {requiredTool} is not authorized for {participation.Stage}/{participation.Role}.");
            foreach (var assignment in version.Skills)
            {
                var applicable = assignment.SkillVersion.Applicability
                    .Where(x => x.Stage == participation.Stage).ToList();
                foreach (var contentType in contentTypes)
                    if (!applicable.Any(x => x.ContentType == contentType))
                        failures.Add(
                            $"Skill {assignment.SkillVersion.Package.Slug} is not applicable to {contentType}/{participation.Stage}.");
                foreach (var required in applicable.SelectMany(x => Strings(x.RequiredToolsJson)).Distinct())
                    if (!tools.Contains(required, StringComparer.Ordinal))
                        failures.Add($"Required tool {required} is outside agent policy.");
            }
        }
        return failures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyList<string> RuntimeTools(string stage, string role)
    {
        var read = stage switch
        {
            "researchPlanning" or "outline" =>
                new[] { "search_corpus", "load_evidence_page", "get_brief_context",
                    "get_specialist_artifacts", "activate_skill", "read_skill_resource" },
            "section" or "repair" =>
                ["load_evidence_page", "get_brief_context", "get_outline_context",
                    "get_completed_section_summaries", "activate_skill", "read_skill_resource",
                    "get_specialist_artifacts"],
            "complete" => [],
            _ => ["load_evidence_page", "get_brief_context", "get_outline_context",
                "get_specialist_artifacts", "activate_skill", "read_skill_resource"],
        };
        if (stage == "complete") return read;
        var terminal = role switch
        {
            "contributor" => "submit_contribution",
            "reviewer" => "submit_review",
            _ => stage switch
            {
                "researchPlanning" => "submit_research_plan", "outline" => "submit_outline",
                "section" => "submit_section", "repair" => "submit_repair",
                "validation" => "submit_validation", _ => "submit_final_synthesis",
            },
        };
        return read.Append(terminal).ToList();
    }

    private static IReadOnlyList<string> Strings(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
