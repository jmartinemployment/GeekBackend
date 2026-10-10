using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Partner;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekApplication.Interfaces.ContentWriterV3;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The writer and the SEO scorer must mean the same thing by "the keyword".
/// </summary>
/// <remarks>
/// <para>
/// A create's Topic is <c>"descriptor: keyword"</c>. <c>GcwSeoAnalyzer</c> has scored against
/// <c>GccTargetKeyword.FromTopic</c> — the half after the colon — since 2026-09-28.
/// <c>BuildMinimalContext</c> was never moved with it and kept setting
/// <c>TargetKeyword: topic</c>, the whole string.
/// </para>
/// <para>
/// So every SEO instruction the writer received asked it to put <i>"Accounts Payable: Automated Data
/// Entry &amp; Processing"</i> verbatim into an H2 and into the opening sentence. No readable prose
/// does that, so the phrase appeared nowhere and the report came back with <b>keyword in lede,
/// keyword in a heading and density ≈ 0.00%</b> all failing on a draft that discussed the keyword
/// throughout (Jeff, 2026-10-02).
/// </para>
/// <para>
/// This asserts on the prompt the provider was actually handed. Asserting on a hand-built
/// <c>ProjectGenerationContext</c> would pass either way — the defect is in what populates it, so the
/// test has to run a real generate.
/// </para>
/// </remarks>
public class WriterIsToldTheKeywordNotTheTopicTests
{
    private const string Topic = "Accounts Payable: Automated Data Entry & Processing";
    private const string Keyword = "Automated Data Entry & Processing";

    private static readonly PartnerPageExtraction GroundablePage = new(
        Citables: null, Advertisements: null, Comparisons: null, Alternatives: null, Pricing: null,
        Icp: null,
        Integrations: [new PartnerIntegrationItem("Xero", "accounting", null, null)],
        Faqs: [new PartnerFaqItem("Does it capture line items?", "Yes.", null)],
        CaseStudies: null, Testimonials: null, Awards: null,
        FeatureInventory: [new PartnerFeatureItem("Invoice data capture", "automation", null, null)],
        TechnicalConstraints: null, OfferCtas: null, Disqualifiers: null, UseCasePlaybooks: null,
        Categories: null, BattlecardSlices: null, DemoBeats: null,
        ComplianceSnippets: null);

    /// <summary>
    /// The long-form path, where the create's Topic <i>is</i> the keyword source.
    /// </summary>
    /// <remarks>
    /// Not the tool path. <c>GenerateToolPageAsync</c> builds its context from the <b>product name</b>
    /// ("Dext"), so <c>FromTopic</c> returns it unchanged and the whole-topic defect never reached it.
    /// A tool page's keyword being the product while the scorer measures the create's topic keyword is
    /// a real and separate disagreement, recorded rather than silently decided here.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> BlogPromptsAsync()
    {
        var fixtures = GccToolPageFanOutFixture.Build(
            GroundablePage, ["https://dext.com"], pagesPerPartner: 2, draftable: true);

        Assert.Equal(Topic, fixtures.Create.Topic);

        await Assert.ThrowsAnyAsync<Exception>(() => fixtures.Service.GenerateStartingContentAsync(
            fixtures.Create with { StartingContentType = "blog" },
            null,
            ContentGeneratorProvider.OpenAi,
            CancellationToken.None));

        Assert.NotEmpty(fixtures.Calls.Prompts);
        return fixtures.Calls.Prompts;
    }

    [Fact]
    public async Task The_keyword_the_writer_is_given_is_the_half_after_the_colon()
    {
        var prompts = await BlogPromptsAsync();

        // The scorer's own notion of the keyword, stated to the writer.
        Assert.Contains(prompts, p => p.Contains($"\"{Keyword}\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_prompt_asks_for_the_whole_topic_as_the_keyword()
    {
        var prompts = await BlogPromptsAsync();

        // Deliberately narrow: the full topic SHOULD still appear in prompts as subject-area context
        // (ProjectName, DetectedFocus) -- that is the entire reason a topic is written with a
        // descriptor. What must never appear is the full topic quoted as the keyword to place.
        Assert.DoesNotContain(prompts, p => p.Contains($"\"{Topic}\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_keyword_placement_rules_reach_the_writer_at_all()
    {
        // Guards the other half: these rules were once spread across prompts that do not write the
        // thing they constrain -- "keyword in lede" sat in the outline prompt, which plans and never
        // writes a lede. A prompt naming the right keyword in no rule is no better.
        var prompts = await BlogPromptsAsync();
        var all = string.Join("\n\n", prompts);

        // Either placement rule, not a specific one: SeoOutlineInstruction ("at least one H2
        // contains") and SeoLedeInstruction ("the opening contains") belong to different stages, and
        // pinning one makes this a test of which prompt happens to be built first. Both phrase it as
        // `contains "<keyword>"`, which is the part that must be true of whichever reaches the writer.
        Assert.Contains($"contains \"{Keyword}\"", all, StringComparison.Ordinal);
        Assert.True(
            all.Contains("HEADINGS AND THE KEYWORD", StringComparison.Ordinal)
            || all.Contains("KEYWORD AND ANSWER", StringComparison.Ordinal),
            "no keyword placement rule reached the writer at all");
    }
}
