using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// Every tool page asks for a block quotation of the partner, in their own words, cited to their
/// own page. Jeff, 2026-09-26: "I want a blockquote in each tool".
///
/// <para>
/// A tool page is an advertisement for that partner, which is what makes the quote box belong on
/// it -- so it is a required element of the type, not an option the writer weighs. These assert the
/// prompt asks unconditionally; <see cref="GeekBackend.Tests.ContentCreator.GccToolQuoteGuardTests"/>
/// asserts the code that refuses a draft without one, because an instruction is not enforcement.
/// </para>
/// </summary>
public class ToolPageQuotationTests
{
    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "accounts payable automation",
        Department: "marketing",
        SiteName: "Acme",
        DetectedTone: string.Empty,
        DetectedFocus: string.Empty,
        // The publisher's own site is the only prose on the ToolPageGenerator path, and the same
        // block that permits a partner quote forbids quoting this.
        CrawledHeadings: ["How we work"],
        CrawledParagraphs: ["We deploy finance automation for mid-market teams."],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: "Geek",
        PublisherLogoUrl: "https://geek.test/logo.png",
        AuthorName: "Author",
        ArticleBaseUrl: "https://geek.test/articles",
        BlogBaseUrl: "https://geek.test/blog",
        ToolBaseUrl: "https://geek.test/tools",
        ImplementerPositioning: "an AI implementation partner",
        Provider: LlmProviderType.OpenAi);

    private static string ToolPrompt(string? researchJson)
    {
        var context = Context();
        var app = new SoftwareApplicationDescriptor("Tipalti", "Payables automation.");
        var request = new ContentPromptBuilder().BuildToolBodyPrompt(
            context,
            new ArticleMetadataDraft("Tipalti", "Meta", ["ai"], []),
            app,
            "tipalti",
            ToolPrompts.Outline(context, app.Name),
            revisionNotes: null,
            extractedToolResearchJson: researchJson,
            lede: null);
        return string.Join("\n", request.Messages.Select(m => m.Content));
    }

    [Fact]
    public void The_page_is_told_it_carries_a_block_quotation()
    {
        var system = ToolPrompt("""{"testimonials":[{"quoteText":"We cut approval time."}]}""");

        Assert.Contains("this page carries exactly one block quotation", system, StringComparison.Ordinal);
        Assert.Contains("and it is required", system, StringComparison.Ordinal);
    }

    [Fact]
    public void The_words_are_pinned_to_the_evidence_and_the_cite_to_its_source()
    {
        var system = ToolPrompt("""{"testimonials":[{"quoteText":"We cut approval time."}]}""");

        Assert.Contains("copied character for character", system, StringComparison.Ordinal);
        Assert.Contains("the URL that evidence gives as their source", system, StringComparison.Ordinal);
    }

    [Fact]
    public void A_near_quote_is_named_as_the_thing_it_may_not_be()
    {
        // The failure guarded against is not an absent quote, it is a near one presented as exact.
        var system = ToolPrompt("{}");

        Assert.Contains("a paraphrase tidied into quotation marks", system, StringComparison.Ordinal);
        Assert.Contains("a claim you are confident they make", system, StringComparison.Ordinal);
        Assert.Contains("wording assembled from several places", system, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prompt_says_the_draft_is_rejected_rather_than_published_with_an_invented_quote()
    {
        // The prompt and GccToolQuoteGuard have to describe the same consequence, or the writer is
        // being asked to guess which one is real.
        var system = ToolPrompt("{}");

        Assert.Contains("the draft is rejected rather than published with an invented one", system, StringComparison.Ordinal);
    }

    [Fact]
    public void The_requirement_is_unconditional()
    {
        // There is no evidence-shaped escape hatch: the old wording had a branch that told the page
        // to carry no quotation, which is the opposite of the requirement.
        foreach (var research in new[] { null, "{}", """{"name":"Tipalti","href":"https://tipalti.com"}""" })
        {
            var system = ToolPrompt(research);
            Assert.Contains("QUOTE Tipalti ONCE, IN THEIR OWN WORDS", system, StringComparison.Ordinal);
            Assert.DoesNotContain("carries no block quotation", system, StringComparison.Ordinal);
        }
    }
}
