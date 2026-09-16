using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.AgentTests;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed class GccV2FirstPartyAgentSeeder(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    GccV2AgentTestWake testWake,
    ILogger<GccV2FirstPartyAgentSeeder> logger) : IHostedService
{
    private static readonly IReadOnlyList<string> AllStages =
        ["researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis", "complete"];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        using var scope = scopes.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        try
        {
            var agents = await repo.ListAgentsAsync(ct: cancellationToken);
            var skills = await repo.ListSkillsAsync("published", ct: cancellationToken);
            foreach (var definition in Definitions(skills))
            {
                var agent = agents.SingleOrDefault(x => x.Slug == definition.Slug)
                    ?? await repo.CreateAgentAsync(new(
                        definition.Slug, definition.Name, definition.Description, true,
                        "system:first-party-seed", null, "startup-seed"), cancellationToken);
                var version = agent.Versions.SingleOrDefault(v => v.SemanticVersion == definition.Version);
                if (version is null)
                {
                    version = await repo.CreateAgentVersionAsync(agent.Id, new(
                        definition.Version, definition.Instructions, definition.ContentTypes,
                        ["search_corpus", "load_evidence_page", "get_brief_context", "get_outline_context",
                            "get_completed_section_summaries", "get_specialist_artifacts",
                            "activate_skill", "read_skill_resource", "submit_contribution", "submit_review",
                            "submit_research_plan", "submit_outline", "submit_section", "submit_repair",
                            "submit_validation", "submit_final_synthesis"],
                        [ContentModelPolicy.O1Pro, ContentModelPolicy.O3],
                        [definition.SkillVersionId], definition.Participation,
                        "system:first-party-seed", null, "startup-seed",
                        definition.Objective, ContentModelPolicy.CurrentVersion), cancellationToken);
                    version = await repo.ReviewAgentVersionAsync(version.Id,
                        new(true, "Reviewed first-party specialist.", "system:first-party-seed", null, "startup-seed"),
                        cancellationToken);
                }
                if (version.State is not ("approved" or "published")) continue;
                var history = await repo.GetAgentTestHistoryAsync(version.Id, cancellationToken);
                var latest = history.FirstOrDefault();
                if (version.State == "approved"
                    && latest?.Status == "passed" && latest.VersionDigest == version.VersionDigest)
                {
                    await repo.TransitionAgentVersionAsync(version.Id, "publish",
                        new("system:first-party-seed", null, null, "startup-seed"), cancellationToken);
                    continue;
                }
                if (latest?.Status is "queued" or "running")
                {
                    testWake.Wake(latest.Id);
                    continue;
                }
                if (version.State == "published" && latest is not null) continue;
                var run = await repo.QueueAgentTestRunAsync(version.Id, new(
                    version.State == "approved" ? "first-party-seed" : "first-party-audit",
                    """{"realExecutionRequired":false}""",
                    "system:first-party-seed", null, "startup-seed"), cancellationToken);
                testWake.Wake(run.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "First-party specialist agent seed failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static IEnumerable<Definition> Definitions(IReadOnlyList<GccV2SkillPackageDto> packages)
    {
        yield return Build("writing", "Writing", "Canonical content producer.",
            "Produce the canonical stage output using only approved evidence and explicitly assigned skills.",
            "citation-discipline", "producer", packages);
        yield return Build("marketing", "Marketing", "Audience, positioning, brand, and conversion specialist.",
            "Contribute audience and positioning constraints; review brand and conversion alignment.",
            "brand-voice", "contributor-reviewer", packages);
        yield return Build("seo", "SEO", "Search intent and on-page discoverability specialist.",
            "Contribute search-intent structure; review natural keyword and heading alignment.",
            "seo-fundamentals", "contributor-reviewer", packages);
        yield return Build("aeo", "AEO", "Answer-engine clarity and extractability specialist.",
            "Contribute direct-answer structure; review self-contained answer clarity.",
            "geo-direct-answer", "contributor-reviewer", packages);
    }

    private static Definition Build(
        string slug, string name, string description, string instructions, string skillSlug,
        string role, IReadOnlyList<GccV2SkillPackageDto> packages)
    {
        var package = packages.Single(x => x.Slug == skillSlug);
        var skill = package.Versions.Single(x => x.State == "published");
        var contentTypes = skill.Applicability.Select(x => x.ContentType)
            .Distinct(StringComparer.Ordinal).Order().ToList();
        IReadOnlyList<CreateGccV2AgentStageParticipation> participation = role == "producer"
            ? AllStages.Select((stage, index) => new CreateGccV2AgentStageParticipation(stage, "producer", 100 + index)).ToList()
            :
            [
                new("researchPlanning", "contributor", 10),
                new("outline", "contributor", 20),
                new("section", "contributor", 30),
                new("finalSynthesis", "contributor", 40),
                new("validation", "reviewer", 200),
                new("complete", "reviewer", 210),
            ];
        return new(slug, name, description, $"{description} Fulfill the assigned specialist role.", "1.2.0",
            instructions, skill.Id, contentTypes, participation);
    }

    private sealed record Definition(
        string Slug, string Name, string Description, string Objective, string Version, string Instructions,
        Guid SkillVersionId, IReadOnlyList<string> ContentTypes,
        IReadOnlyList<CreateGccV2AgentStageParticipation> Participation);
}
