using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The opening asks nothing of the reader, and is not told to name a tool (Jeff, 2026-10-07).
///
/// <para>
/// The Melio tool page's opening was refused because it linked the vendor's homepage. The prompt it was
/// written from printed, three times, the code of a call-to-action choice the brief then carried, and
/// nothing told the writer the opening is not where the page asks. The brief has had no such field since
/// 2026-10-10. The pillar's opening was told two opposite things at once: its own
/// rule is that it names no partner or tool, and its research brief said to name each tool wherever it is
/// relevant and to link it on first mention.
/// </para>
/// </summary>
public class GccOpeningAsksNothingTests
{
    private const string Ask = "THE OPENING ASKS NOTHING OF THE READER";

    internal static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "automated approval workflows",
        Department: "accounting",
        SiteName: "Acme",
        DetectedTone: string.Empty,
        DetectedFocus: string.Empty,
        CrawledHeadings: [],
        CrawledParagraphs: [],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: "Geek @ Your Spot",
        PublisherLogoUrl: "https://geek.test/logo.png",
        AuthorName: "Geek @ Your Spot Editorial Team",
        ArticleBaseUrl: "https://geek.test/articles",
        BlogBaseUrl: "https://geek.test/blog",
        ToolBaseUrl: "https://geek.test/tools",
        ImplementerPositioning: "Geek @ Your Spot is an AI implementation consultancy for small businesses located in West Palm Beach, Broward and Miami-Dade counties.",
        Provider: GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi,
        KnownCrawlTools: [new KnownCrawlTool("Ramp", null, "/tools/accounting/accounts-payable/ramp")],
        PrimaryIntent: "commercial_investigation",
        ContentAngle: "problem_solution",
        ToneOfVoice: "consultant_professional");

    private static readonly ArticleMetadataDraft Article = new("Title", "Meta", ["ai"], ["a", "b"]);
    private static readonly BlogMetadataDraft Blog = new("Title", "Meta", ["ai"], ["a", "b"]);
    private static readonly ArticleDraft SourceArticle = new(
        "Title", "Meta", ContentDocumentText.FromPlainText("Body."), ["ai"], 1, []);

    private static string Prompt(ChatCompletionRequest r) =>
        string.Join("\n", r.Messages.Select(m => m.Content));

    public static TheoryData<string> EveryLedePrompt => new() { "article", "pillar", "blog", "standaloneBlog" };

    private static ChatCompletionRequest Build(string which)
    {
        var b = new ContentPromptBuilder();
        return which switch
        {
            "article" => b.BuildArticleLedePrompt(Context(), Article),
            "pillar" => b.BuildPillarLedePrompt(
                Context(), Article, "the opening", 0, 6,
                [SectionSlot.Cover("the opening"), SectionSlot.Cover("what it costs")], isRegeneration: false),
            "blog" => b.BuildBlogLedePrompt(Context(), SourceArticle, Blog),
            "standaloneBlog" => b.BuildStandaloneBlogLedePrompt(Context(), Blog),
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "unknown lede prompt"),
        };
    }

    [Theory]
    [MemberData(nameof(EveryLedePrompt))]
    public void Every_opening_is_told_it_asks_nothing_of_the_reader(string which)
    {
        var prompt = Prompt(Build(which));

        Assert.Contains(Ask, prompt, StringComparison.Ordinal);
        Assert.Contains("does not ask them to book, call, sign up or click", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("A link in the opening goes only to", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryLedePrompt))]
    public void No_opening_is_told_anything_about_links_because_its_reply_cannot_carry_one(string which)
    {
        // "The opening carries no links" was said once, then a second time after the evidence block,
        // because the block's own rule told the writer to link every claim and won (100% of pages,
        // run against ac124a3, 2026-10-09). The writer links nothing now (Jeff, 2026-10-10): the reply
        // has no field for a link, and GccToolLinker is handed the body, never the opening.
        var prompt = Prompt(Build(which));

        Assert.DoesNotContain("The opening carries no links", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"link\"", prompt, StringComparison.Ordinal);
    }

    private const string Evidence =
        "=== QUOTEABLE RESEARCH (partner/tool evidence) ===\n" +
        "Partner Widget (https://partner.test/widget)\n" +
        "  - Widget cuts invoice processing time in half.";

    public static TheoryData<string> EveryLiveLedePrompt => new() { "article", "pillar", "standaloneBlog" };

    private static ChatCompletionRequest BuildWithEvidence(string which)
    {
        var b = new ContentPromptBuilder();
        return which switch
        {
            "article" => b.BuildArticleLedePrompt(Context(), Article, evidenceBlock: Evidence),
            "pillar" => b.BuildPillarLedePrompt(
                Context(), Article, "the opening", 0, 6,
                [SectionSlot.Cover("the opening"), SectionSlot.Cover("what it costs")], isRegeneration: false,
                evidenceBlock: Evidence),
            "standaloneBlog" => b.BuildStandaloneBlogLedePrompt(Context(), Blog, Evidence),
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "unknown lede prompt"),
        };
    }

    /// <summary>
    /// The evidence still reaches the opening, ahead of its assignment, and nothing about links is
    /// restated after it. The reminder that used to sit there answered a rule the evidence block no
    /// longer carries.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryLiveLedePrompt))]
    public void The_evidence_reaches_the_opening_before_its_assignment_with_no_link_reminder_after_it(string which)
    {
        var prompt = Prompt(BuildWithEvidence(which));

        var evidenceIndex = prompt.IndexOf(Evidence, StringComparison.Ordinal);
        var assignmentIndex = prompt.IndexOf("=== ASSIGNMENT ===", StringComparison.Ordinal);

        Assert.True(evidenceIndex >= 0, "the evidence block itself must reach the prompt");
        Assert.True(assignmentIndex > evidenceIndex, "the assignment comes after the evidence");
        Assert.DoesNotContain("This opening still carries no links at all", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("BEFORE YOU WRITE", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("article")]
    [InlineData("pillar")]
    [InlineData("standaloneBlog")]
    public void The_rest_of_the_brief_still_reaches_the_opening(string which)
    {
        var prompt = Prompt(Build(which));

        Assert.Contains("Primary intent: commercial investigation (the reader is weighing options before buying)", prompt, StringComparison.Ordinal);
        Assert.Contains("Tone of voice: consultant, professional (objective authority)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pillars_opening_is_not_told_to_name_tools_or_given_the_known_tools_block()
    {
        var prompt = Prompt(Build("pillar"));

        Assert.DoesNotContain("KNOWN TOOLS", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/tools/accounting/accounts-payable/ramp", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Name these tools", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pillars_body_sections_still_carry_the_known_tools_block()
    {
        // Only the opening drops it: the body names the tools, with citations, section by section.
        var request = new ContentPromptBuilder().BuildArticleSectionBatchPrompt(
            Context(), Article, [SectionSlot.Cover("what it costs")], [SectionSlot.Cover("what it costs")], isRegeneration: false);

        Assert.Contains("KNOWN TOOLS", Prompt(request), StringComparison.Ordinal);
    }

    [Fact]
    public void The_pillars_tone_line_is_a_tone_and_the_positioning_is_stated_under_its_own_name()
    {
        var prompt = Prompt(Build("pillar"));
        const string positioning = "Geek @ Your Spot is an AI implementation consultancy for small businesses located in West Palm Beach, Broward and Miami-Dade counties.";

        var toneLine = prompt.Split('\n').Single(l => l.StartsWith("Tone:", StringComparison.Ordinal));
        Assert.DoesNotContain("Geek @ Your Spot", toneLine, StringComparison.Ordinal);
        Assert.Contains("consultative", toneLine, StringComparison.Ordinal);
        // The positioning is stated under its own name, not as the tone.
        Assert.Contains($"Publisher positioning: {positioning}", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("consultant_professional", "problem_solution")]
    [InlineData("informational_instructional", "ultimate_guide")]
    public void The_consultant_appendix_has_no_second_voice_no_closing_instruction_and_no_sampling_advice(string tone, string angle)
    {
        var appendix = GccGenerateService.BuildConsultantAppendix(CreateWith(tone, angle));

        Assert.NotEmpty(appendix);
        Assert.DoesNotContain("Voice:", appendix, StringComparison.Ordinal);
        Assert.DoesNotContain("newspaper", appendix, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("first-person plural", appendix, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Close with an FAQ", appendix, StringComparison.Ordinal);
        Assert.DoesNotContain("temperature", appendix, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_consultant_appendix_keeps_the_role_the_four_phases_the_filler_ban_and_the_no_markup_rule()
    {
        var appendix = GccGenerateService.BuildConsultantAppendix(CreateWith("consultant_professional", "problem_solution"));

        Assert.Contains("Write as a Senior IT Consultant advising local SMBs on AI implementation", appendix, StringComparison.Ordinal);
        Assert.Contains("Assume peer-level technical knowledge; high scannability.", appendix, StringComparison.Ordinal);
        Assert.Contains("1. Business Objectives Alignment", appendix, StringComparison.Ordinal);
        Assert.Contains("2. Data Quality Assessment", appendix, StringComparison.Ordinal);
        Assert.Contains("3. Tech Selection & Architecture", appendix, StringComparison.Ordinal);
        Assert.Contains("4. Pilot Implementation Strategy", appendix, StringComparison.Ordinal);
        Assert.Contains("ban AI filler / clichés", appendix, StringComparison.Ordinal);
        Assert.Contains("Emit no markup of any kind", appendix, StringComparison.Ordinal);
    }

    private static GccCreateDto CreateWith(string tone, string angle) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "pillar", "Accounts Payable: Automated Approval Workflows",
        null, null, null, $$"""{"toneOfVoice":"{{tone}}","angle":"{{angle}}"}""", null,
        "draft", DateTime.UtcNow, DateTime.UtcNow);
}
