using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// A named tool links to its own page under /tools. Jeff, 2026-09-26: "Tool mentions should be Next
/// js &lt;Links&gt; to /tools".
///
/// <para>
/// The site decides Link-versus-anchor from the href alone -- a relative path renders as a Next
/// <c>Link</c>, anything matching <c>^https?:</c> renders as an external anchor -- so these assert
/// the relative public path reaches the prompt and that the vendor URL is refused. Asserted on the
/// rendered prompt: the brief is built by an internal class and reaches the model only through here.
/// </para>
/// </summary>
public class ContentPromptBuilderToolLinkTests
{
    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "accounts payable automation",
        Department: "marketing",
        SiteName: "Acme",
        DetectedTone: string.Empty,
        DetectedFocus: string.Empty,
        CrawledHeadings: [],
        CrawledParagraphs: [],
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
        Provider: LlmProviderType.OpenAi,
        KnownCrawlTools:
        [
            new KnownCrawlTool("Tipalti", "https://tipalti.com/pricing"),
            new KnownCrawlTool("Stripe Tax", null),
        ]);

    private static string BlogPrompt(ProjectGenerationContext context) =>
        string.Join("\n", new ContentPromptBuilder()
            .BuildStandaloneBlogBodyPrompt(context, new BlogMetadataDraft("Title", "Meta", ["ai"], ["Overview"]))
            .Messages.Select(m => m.Content));

    [Fact]
    public void A_known_tool_carries_its_own_public_path()
    {
        var system = BlogPrompt(Context());

        Assert.Contains("/tools/marketing/tipalti", system, StringComparison.Ordinal);
        Assert.Contains("/tools/marketing/stripe-tax", system, StringComparison.Ordinal);
    }

    [Fact]
    public void Linking_a_named_tool_is_required_not_optional()
    {
        // "A link is optional" was the whole reason the drafts named tools and linked none of them.
        var system = BlogPrompt(Context());

        Assert.Contains("a named tool links to it", system, StringComparison.Ordinal);
        Assert.Contains("first substantive body mention", system, StringComparison.Ordinal);
        Assert.DoesNotContain("A link is optional", system, StringComparison.Ordinal);
    }

    [Fact]
    public void The_path_is_relative_so_the_site_renders_a_next_link()
    {
        // An absolute href renders as a plain external anchor on the site, not a Next Link, which
        // is exactly the mention Jeff asked not to get.
        var system = BlogPrompt(Context());

        Assert.Contains("relative and starts with a slash", system, StringComparison.Ordinal);
        Assert.Contains("Do not link a tool to the vendor's own website", system, StringComparison.Ordinal);
    }

    [Fact]
    public void The_crawl_source_is_named_as_off_limits_not_as_the_destination()
    {
        // The crawl source is where the tool was found, never where the reader is sent.
        var system = BlogPrompt(Context());

        Assert.Contains("https://tipalti.com/pricing", system, StringComparison.Ordinal);
        Assert.Contains("do not send the reader there", system, StringComparison.Ordinal);
    }

    [Fact]
    public void No_known_tools_means_no_tool_link_instruction_at_all()
    {
        // Nothing to link is not a reason to tell the model to link something.
        var system = BlogPrompt(Context() with { KnownCrawlTools = [] });

        Assert.DoesNotContain("KNOWN TOOLS", system, StringComparison.Ordinal);
        Assert.DoesNotContain("a named tool links to it", system, StringComparison.Ordinal);
    }
}
