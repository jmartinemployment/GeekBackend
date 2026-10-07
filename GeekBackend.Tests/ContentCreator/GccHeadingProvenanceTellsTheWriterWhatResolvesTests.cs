using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The guard matches provenance tags by exact set membership, so the first attempt has to be told which
/// values are in the set, and no example in the instructions may be a tag that cannot resolve.
/// </summary>
/// <remarks>
/// The 2026-10-07 blog was refused for two headings tagged <c>brief:topic</c>, which is the example the
/// instruction itself gave and never a licensable field. A retry that listed the valid values used to
/// rescue that; with no retry it is a first-attempt refusal unless the writer is told up front.
/// </remarks>
public class GccHeadingProvenanceTellsTheWriterWhatResolvesTests
{
    private static GccHeadingProvenanceEvidence Evidence() => new(
        PopulatedBriefFields: new HashSet<string>(["primaryIntent", "angle"], StringComparer.OrdinalIgnoreCase),
        PaaQuestions: new HashSet<string>(["How long does setup take?"], StringComparer.OrdinalIgnoreCase),
        CompetitorHeadings: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        SiteSubtopics: new HashSet<string>(["Invoice capture"], StringComparer.OrdinalIgnoreCase),
        RetrievedEvidence: new HashSet<string>(["Stampli"], StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void The_valid_values_are_listed_by_kind_and_an_empty_kind_says_so()
    {
        var text = GccHeadingProvenanceGuard.LicensedValues(Evidence());

        Assert.Contains("=== THE ONLY VALUES THAT LICENSE A HEADING ===", text, StringComparison.Ordinal);
        Assert.Contains("brief: \"primaryIntent\" | \"angle\"", text, StringComparison.Ordinal);
        Assert.Contains("paa: \"How long does setup take?\"", text, StringComparison.Ordinal);
        Assert.Contains("site: \"Invoice capture\"", text, StringComparison.Ordinal);
        Assert.Contains("evidence: \"Stampli\"", text, StringComparison.Ordinal);
        Assert.Contains("competitor: none available", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_instructions_own_brief_example_is_a_tag_that_can_resolve()
    {
        var instruction = string.Join("\n", new ContentPromptBuilder().BuildStandaloneBlogBodyPrompt(
            new ProjectGenerationContext(
                "Acme", "https://acme.test", "ai implementation", "marketing", "Acme", "", "", [], [], null, [], [],
                "Geek", "https://geek.test/logo.png", "Author", "https://geek.test/articles",
                "https://geek.test/blog", "https://geek.test/tools", "an AI implementation partner",
                LlmProviderType.OpenAi),
            new BlogMetadataDraft("T", "M", ["ai"], ["a"]),
            sectionBatch: [SectionSlot.Assigned("a")],
            requireHeadingProvenance: true).Messages.Select(m => m.Content));

        // "topic" is not a brief field the guard licenses; naming it as the example is how it got copied.
        Assert.DoesNotContain("brief:topic", instruction, StringComparison.Ordinal);
        Assert.Contains("brief:primaryIntent", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_tagged_with_a_listed_value_resolves_and_the_topic_does_not()
    {
        var evidence = Evidence();
        Section Heading(string tag) =>
            new("h2", "A heading", [], null, [], Provenance: tag);

        Assert.Empty(GccHeadingProvenanceGuard.FindUnlicensedHeadings([Heading("brief:primaryIntent")], evidence));
        Assert.NotEmpty(GccHeadingProvenanceGuard.FindUnlicensedHeadings([Heading("brief:topic")], evidence));
    }
}
