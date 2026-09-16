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
            foreach (var definition in Definitions())
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
                        [], definition.Participation,
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

    // Craft rules, folded in from the former skill package layer. Skills were separately versioned,
    // digest-pinned packages that added change-control ceremony without adding capability: the text is
    // guidance for the agent that owns it (plans/agent-specialists.md §4.5).
    private const string Grounding = """
        Keep every factual claim within the supplied evidence and never invent a citation. Prefer exact
        primary-source passages with stable page identifiers. Return verbatim quote citations for
        factual claims, and fail any factual claim lacking verified quote-level support.
        """;

    private const string Voice = """
        Apply the supplied brand voice without changing facts, evidence rules, or security policy. Use
        brand context only as style guidance; never broaden source scope. Keep terminology, tone, and
        prohibited-language constraints consistent.
        """;

    private const string NoRepeat = """
        Give each section a distinct job and avoid repeating prior section substance. Use
        completed-section summaries only to detect overlap. Remove redundant claims while preserving
        unique cited evidence.
        """;

    private const string Cta = """
        Use only the canonical conversion objective and approved call to action. Never manufacture
        urgency or unsupported promises. Keep the CTA proportionate to the evidence and audience stage.
        """;

    private const string Search = """
        Use descriptive headings and answer the primary search intent without keyword stuffing.
        Prioritize evidence that directly answers the target keyword and related questions. Include a
        clear information hierarchy and concise descriptive headings.
        """;

    private const string DirectAnswer = """
        Lead applicable sections with a concise direct answer before supporting detail. Prefer
        authoritative quotable evidence suitable for answer extraction. Use self-contained statements
        and explicit entity names.
        """;

    private const string TechnicalDepth = """
        Explain architecture, tradeoffs, constraints, and operational implications precisely. Prefer
        technical documentation and concrete implementation evidence. Include prerequisites,
        boundaries, failure modes, and decisions where supported.
        """;

    private static string Producer(string craft) =>
        string.Join("\n\n", ["Produce the canonical stage output for your formats.", craft,
            Grounding, Voice, NoRepeat, Cta]);

    private static IEnumerable<Definition> Definitions()
    {
        // ---- Producers: exactly one owns each content type ----

        yield return New("article", "Article", "Search-led article specialist.",
            ["blog", "guide", "listicle"], "producer",
            Producer(string.Join("\n\n", [Search, DirectAnswer])));

        yield return New("technical-article", "Technical Article", "Implementation and tradeoff specialist.",
            ["tech-article"], "producer", Producer(TechnicalDepth));

        yield return New("pillar", "Pillar", "Long-form authority specialist.",
            ["pillar", "whitepaper"], "producer",
            Producer(string.Join("\n\n", [Search, DirectAnswer, TechnicalDepth])));

        yield return New("comparison", "Comparison", "Evidence-symmetric comparison specialist.",
            ["comparison", "alternatives"], "producer",
            Producer("""
                Compare options using the same evidence-backed criteria and neutral language. Retrieve
                both partner and competitor evidence for each criterion. Separate verified differences
                from analysis and avoid unsupported superiority claims. A competitor is a rival
                consultancy or a content competitor, never a product; software is a partner.
                """));

        yield return New("case-study", "Case Study", "Measured-outcome proof specialist.",
            ["case-study"], "producer",
            Producer("""
                Separate context, intervention, and measured outcome; do not imply causation without
                evidence. Prioritize first-party proof, measured outcomes, and explicit timeframes.
                Label unverified outcomes and preserve qualifications from sources.
                """));

        yield return New("tool-page", "Tool Page", "Partner tool page specialist.",
            ["tool"], "producer",
            Producer(string.Join("\n\n", [Search, TechnicalDepth,
                """
                A partner is a third-party SaaS product the operator recommends and implements, never
                resells. Ground pricing, features, integrations and limits in the partner library; never
                infer a tier, a price, or a capability the evidence does not state.
                """])));

        yield return New("service-local", "Service & Local", "Service and local landing specialist.",
            ["service", "local"], "producer", Producer(string.Join("\n\n", [Search, Cta])));

        yield return New("email", "Email", "Lifecycle and campaign email specialist.",
            ["email"], "producer",
            Producer("""
                Write a single-purpose email with one ask. Earn the open with a specific subject line and
                a preview that does not repeat it. Keep to the sequence position you were given: a first
                touch establishes relevance, a follow-up advances it, never restate the whole case.
                Short paragraphs, one idea each. This is not a trimmed article.
                """));

        yield return New("social", "Social", "Short-form social specialist.",
            ["social"], "producer",
            Producer("""
                Lead with a hook in the first line that works with no context. One idea per post. Respect
                the platform's length and conventions. Do not produce a listicle unless asked, and never
                pad to length.
                """));

        yield return New("ads", "Ads", "Paid advertising variation specialist.",
            ["ads"], "producer",
            Producer("""
                Produce genuinely distinct angles rather than reworded duplicates: vary the pain, the
                proof, or the outcome, not just the adjectives. Keep every claim inside the supplied
                evidence. Respect platform character limits exactly.
                """));

        yield return New("image-prompt", "Image Prompt", "Visual prompt specialist.",
            ["image-prompt"], "producer",
            string.Join("\n\n", ["Produce the canonical stage output for your formats.",
                """
                Specify subject, composition, lighting, style, and explicit negative constraints. Never
                invent brand marks, logos, or real people. Describe only what the brief and evidence
                support.
                """, Grounding, Voice]));

        yield return New("pdf-carousel", "PDF / Carousel", "Slide-sequence teaching specialist.",
            ["linkedin-document"], "producer",
            Producer("""
                Structure a concise hook, a progressive teaching sequence, a summary, and the approved
                CTA. Allocate one evidence-backed idea per slide or panel. Keep panels scannable and
                preserve citation traceability in supporting metadata.
                """));

        // ---- Reviewers: ride along where applicable ----

        yield return New("marketing", "Marketing", "Audience, positioning, brand, and conversion specialist.",
            AllContentTypes, "contributor-reviewer",
            string.Join("\n\n", ["Contribute audience and positioning constraints; review brand and conversion alignment.",
                Voice, Cta]));

        yield return New("seo", "SEO", "Search intent and on-page discoverability specialist.",
            ["pillar", "blog", "guide", "tech-article", "case-study", "whitepaper", "listicle",
                "comparison", "alternatives", "tool", "service", "local"],
            "contributor-reviewer",
            string.Join("\n\n", ["Contribute search-intent structure; review natural keyword and heading alignment.", Search]));

        yield return New("aeo", "AEO", "Answer-engine clarity and extractability specialist.",
            ["pillar", "blog", "guide", "tech-article", "whitepaper", "comparison", "alternatives",
                "tool", "service", "local"],
            "contributor-reviewer",
            string.Join("\n\n", ["Contribute direct-answer structure; review self-contained answer clarity.", DirectAnswer]));

        yield return New("claims-disclosure", "Claims & Disclosure", "Claim safety and affiliate disclosure specialist.",
            AllContentTypes, "contributor-reviewer",
            """
            Review every claim that names a third party. Never echo a rival's claim-risk text as
            verified fact. A named partner requires a verified citation in the same section. Surface the
            affiliate disclosure with its stated jurisdiction or policy, and never assert a competitor
            deficit that is not quoted from a boundary the rival states about itself - an absence of
            mention is not a deficit.
            """);
    }

    private static readonly IReadOnlyList<string> AllContentTypes =
        ["blog", "pillar", "tool", "comparison", "case-study", "guide", "alternatives", "tech-article",
            "listicle", "service", "local", "whitepaper", "email", "social", "image-prompt", "ads",
            "linkedin-document"];

    private static Definition New(
        string slug, string name, string description,
        IReadOnlyList<string> contentTypes, string role, string instructions)
    {
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
        return new(slug, name, description, $"{description} Fulfill the assigned specialist role.",
            "2.0.0", instructions, contentTypes, participation);
    }

    private sealed record Definition(
        string Slug, string Name, string Description, string Objective, string Version, string Instructions,
        IReadOnlyList<string> ContentTypes,
        IReadOnlyList<CreateGccV2AgentStageParticipation> Participation);
}
