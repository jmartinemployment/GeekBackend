using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekApplication.Models.ContentCreator;
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

    private static string ToolPrompt(
        string? researchJson,
        IReadOnlyList<GccQuoteCandidate>? quoteCandidates = null)
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
            lede: null,
            quoteCandidates: quoteCandidates);
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

        // Was "the URL that evidence gives as their source", which pointed at the extraction's
        // provenance. The cite now comes from the span's own printed URL, because the writer picks
        // from the same list the guard checks against -- so the source of the cite moved with it.
        Assert.Contains("the URL printed beside the span you chose", system, StringComparison.Ordinal);
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

    [Fact]
    public void The_writer_is_shown_the_spans_it_may_quote()
    {
        // The guard and the writer have to read one list. The instruction used to point at the
        // extraction JSON -- "a testimonial or an isolated claim" -- so a partner whose extraction
        // filed nothing under those two headings left the writer with nothing verbatim in front of
        // it. It correctly wrote no quotation and the page was then refused for not using spans it
        // had never seen: "28 quotable partner span(s) were supplied and none was used".
        var candidates = GccQuoteCandidates.From([
            new GccGroundedPassage(
                "https://partner.test/customers", "Customers",
                [new TextParagraph([new Run(
                    "We cut approval time from nine days to two, and nobody has looked back.")])]),
        ]);

        var prompt = ToolPrompt(null, candidates);

        Assert.Contains("QUOTABLE SPANS", prompt, StringComparison.Ordinal);
        Assert.Contains("nine days to two", prompt, StringComparison.Ordinal);
        Assert.Contains("[cite: https://partner.test/customers]", prompt, StringComparison.Ordinal);

        // The old wording pointed somewhere else and must not survive beside the list.
        Assert.DoesNotContain("A testimonial or an isolated claim is what this is for",
            prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_spans_the_list_is_absent_rather_than_empty()
    {
        // An empty "QUOTABLE SPANS:" header invites the writer to invent one to fill it.
        Assert.DoesNotContain("QUOTABLE SPANS --", ToolPrompt(null, []), StringComparison.Ordinal);
    }

    [Fact]
    public void The_writer_is_told_the_quote_must_say_how_the_tool_solves_the_problem()
    {
        // Jeff, 2026-10-01: "The quote the application is suppose to return is how Tool x solves
        // problem y." The instruction said only "the span that best supports a point the page actually
        // makes" -- which is any sentence on the partner's site. The candidate list is shape-filtered,
        // not meaning-filtered, by design, so with no stated target the writer was choosing from forty
        // arbitrary sentences. That is why a page could be refused with forty spans supplied.
        var candidates = GccQuoteCandidates.From([
            new GccGroundedPassage(
                "https://partner.test/customers", "Customers",
                [new TextParagraph([new Run(
                    "Approval routing runs itself, so invoices clear in two days instead of nine.")])]),
        ]);

        var prompt = ToolPrompt(null, candidates);

        Assert.Contains("how", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("solves the problem", prompt, StringComparison.Ordinal);
        // The target is named, not implied: the page's own keyword is the problem.
        Assert.Contains("manual or status-quo", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_compliment_is_named_as_not_qualifying()
    {
        // The failure mode this replaces preferred a testimonial outright. Praise reads like a great
        // quote and says nothing about the problem, so the instruction has to refuse it by name rather
        // than leave "best supports a point" to be read generously.
        var prompt = ToolPrompt(null, []);

        Assert.Contains("NOT a testimonial", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("best decision we made", prompt, StringComparison.Ordinal);
    }
}
