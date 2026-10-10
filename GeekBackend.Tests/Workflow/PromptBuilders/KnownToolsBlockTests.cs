using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// The tools block names the tools and nothing else. The writer links nothing (Jeff, 2026-10-10): a
/// tool's page on this site is put on the first mention of its name by <c>GccToolLinker</c>, so the
/// block prints no id, no path and no address, and says nothing about linking.
/// </summary>
/// <remarks>
/// Jeff, 2026-09-26: "Tool mentions should be Next js &lt;Links&gt; to /tools". That is still what a
/// reader gets -- <c>GccToolLinkerTests</c> pins the path on the name. What changed is who puts it
/// there: this block told the writer to, in three different shapes, and each one cost pages.
/// </remarks>
public class KnownToolsBlockTests
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
            new KnownCrawlTool("Tipalti", "https://tipalti.com/pricing", "/tools/marketing/tipalti"),
            new KnownCrawlTool("Stripe Tax", null),
        ]);

    private static string BlogPrompt(ProjectGenerationContext context) =>
        string.Join("\n", new ContentPromptBuilder()
            .BuildStandaloneBlogBodyPrompt(context, new BlogMetadataDraft("Title", "Meta", ["ai"], ["Overview"]))
            .Messages.Select(m => m.Content));

    /// <summary>The block's own lines: its header through the line before the next block's header.</summary>
    internal static IReadOnlyList<string> BlockOf(string prompt)
    {
        var lines = prompt.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var start = lines.FindIndex(l => l.StartsWith("=== KNOWN TOOLS", StringComparison.Ordinal));
        if (start < 0) return [];
        var next = lines.FindIndex(start + 1, l => l.StartsWith("===", StringComparison.Ordinal));
        return next < 0 ? lines[start..] : lines[start..next];
    }

    [Fact]
    public void The_block_names_each_tool_on_its_own_line()
    {
        var block = BlockOf(BlogPrompt(Context()));

        Assert.Contains("- Tipalti", block);
        Assert.Contains("- Stripe Tax", block);
    }

    [Fact]
    public void The_block_tells_the_writer_to_discuss_the_tools_and_not_to_list_them()
    {
        var block = string.Join("\n", BlockOf(BlogPrompt(Context())));

        Assert.Contains("Discuss them substantively", block, StringComparison.Ordinal);
        Assert.Contains("Do not produce a roll-call list", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_block_prints_no_id_no_path_and_no_address()
    {
        var block = string.Join("\n", BlockOf(BlogPrompt(Context())));

        Assert.DoesNotContain("[T", block, StringComparison.Ordinal);
        Assert.DoesNotContain("T#", block, StringComparison.Ordinal);
        Assert.DoesNotContain("/tools/", block, StringComparison.Ordinal);
        Assert.DoesNotContain("public path", block, StringComparison.Ordinal);
        // The crawl source is where the tool was found. It was printed as "do not send the reader there";
        // the writer cannot send the reader anywhere now, so it is not printed at all.
        Assert.DoesNotContain("tipalti.com", block, StringComparison.Ordinal);
        Assert.DoesNotContain("http", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_block_says_nothing_about_linking()
    {
        var block = string.Join("\n", BlockOf(BlogPrompt(Context())));

        Assert.DoesNotContain("link", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_known_tools_means_no_block_at_all()
    {
        var prompt = BlogPrompt(Context() with { KnownCrawlTools = [] });

        Assert.DoesNotContain("KNOWN TOOLS", prompt, StringComparison.Ordinal);
        Assert.Empty(BlockOf(prompt));
    }
}
