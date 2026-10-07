using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The writer is told every provenance form the guard accepts, in the shape the guard parses.
///
/// <para>
/// GccHeadingProvenanceGuard resolves five kinds by exact set membership, deliberately without
/// similarity matching. The instruction named four of them and never mentioned "evidence:", which
/// was added to the guard later — so the writer guessed its value format and a blog was refused for
/// "evidence:Lightyear", "brief:Topic / keyword: ..." and "site:... :" (2026-10-01). A guard that
/// matches exactly has to be told exactly what to match.
/// </para>
/// </summary>
public class GccHeadingProvenanceFormsTests
{
    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme", ProjectUrl: "https://acme.test", TargetKeyword: "ai implementation",
        Department: "marketing", SiteName: "Acme", DetectedTone: "", DetectedFocus: "",
        CrawledHeadings: [], CrawledParagraphs: [], JsonLdStructuredSummary: null,
        KeywordSources: [], PeopleAlsoAskQuestions: [], PublisherName: "Geek",
        PublisherLogoUrl: "https://geek.test/logo.png", AuthorName: "Author",
        ArticleBaseUrl: "https://geek.test/articles", BlogBaseUrl: "https://geek.test/blog",
        ToolBaseUrl: "https://geek.test/tools", ImplementerPositioning: "an AI implementation partner",
        Provider: LlmProviderType.OpenAi, ConsultationAnchorHref: "#c", ConsultationCtaLabel: "Book");

    private static string BlogPrompt() =>
        string.Join("\n", new ContentPromptBuilder().BuildStandaloneBlogBodyPrompt(
            Context(),
            new BlogMetadataDraft("Title", "Meta", ["ai"], ["A section"]),
            sectionBatch: [SectionSlot.Assigned("A section")],
            batchIndex: 0,
            requireHeadingProvenance: true).Messages.Select(m => m.Content));

    [Theory]
    [InlineData("plan:")]
    [InlineData("brief:")]
    [InlineData("paa:")]
    [InlineData("competitor:")]
    [InlineData("site:")]
    [InlineData("evidence:")]
    public void Every_form_the_guard_accepts_is_named_to_the_writer(string form)
    {
        Assert.Contains(form, BlogPrompt(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_brief_form_says_field_name_not_the_printed_line()
    {
        // The refusal was brief:"Topic / keyword: Accounts Payable: Automated Data Entry &
        // Processing" -- the rendered line, label and all. The set holds field names.
        var prompt = BlogPrompt();

        // The example is a field the guard licenses. It was "brief:topic" until 2026-10-07, which the
        // guard never accepted, and the writer copied it into two refused headings.
        Assert.Contains("brief:primaryIntent", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("brief:topic", prompt, StringComparison.Ordinal);
        Assert.Contains("not the line as printed", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_site_form_says_no_trailing_punctuation()
    {
        // The refusal was site:"Automated Data Entry & Processing:" -- one trailing colon copied
        // from the label, and an exact-match guard has no way to forgive it.
        Assert.Contains("with no trailing punctuation", BlogPrompt(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_evidence_form_names_what_may_stand_as_a_source()
    {
        // The guard's set holds four identifiers per retrieved passage. The writer was told none of
        // them and invented "evidence:Lightyear".
        var prompt = BlogPrompt();

        Assert.Contains("the partner name, the page title, the section title or the host",
            prompt, StringComparison.Ordinal);
    }
}
