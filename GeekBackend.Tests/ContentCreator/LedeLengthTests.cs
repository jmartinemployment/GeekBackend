using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The opening, and the field that was starving it.
///
/// Jeff, 2026-09-23, twice: "Lede paragraph way to short, bigger font doesn't make it look like
/// more words" and then "Lede paragraph still ridiculously short! Should be 3 x that length it is
/// LAME." Alongside it: "IMAGE PROMPTS ARE STILL INLINE DESPITE BEING TOLD TO REMOVE!"
///
/// One cause behind both. Every lede prompt asked for a 40-400 word imagePrompt inside the same
/// JSON object as the paragraphs, under a 1,024-token ceiling — so the opening competed for budget
/// with a field that <c>GenerateSectionImagePromptsAsync</c> overwrites immediately afterwards from
/// its own dedicated call, and the model leaked the prompt text into the prose it shared an object
/// with. Removing the renderer that displayed the field could never fix that: the words were in the
/// paragraphs, not in the field.
/// </summary>
public class LedeLengthTests
{
    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "automated data entry",
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
        Provider: GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi);

    private static readonly ArticleMetadataDraft Article = new("Title", "Meta", ["ai"], ["a", "b"]);
    private static readonly BlogMetadataDraft Blog = new("Title", "Meta", ["ai"], ["a", "b"]);
    private static readonly ArticleDraft SourceArticle = new(
        "Title", "Meta", ContentDocumentText.FromPlainText("Body."), ["ai"], 1, []);

    private static string Prompt(ChatCompletionRequest r) =>
        string.Join("\n", r.Messages.Select(m => m.Content));

    public static TheoryData<string> EveryLedePrompt => new()
    {
        "article", "pillar", "blog", "standaloneBlog",
    };

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
    public void NoLedePromptAsksForAnImagePrompt(string which)
    {
        // The hero prompt comes from GenerateSectionImagePromptsAsync (Create) and from its own
        // ImagePromptPillarFigure/ImagePromptBlogFigure rows (orchestrator). Asking for it here
        // produced a field that was overwritten seconds later, having already cost the opening the
        // budget it needed -- and leaked into the prose as "Image prompt: A professional, flat
        // vector illustration...".
        var prompt = Prompt(Build(which));

        Assert.DoesNotContain("imagePrompt", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("image-generation model", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(EveryLedePrompt))]
    public void EveryLedePromptStatesTheRulesItsTextIsCheckedAgainst(string which)
    {
        // The opening is checked for money like the rest of the page, and it is written once. So a rule
        // the opening was never told is a refusal nothing can fix. Links are not among them: the writer
        // cannot write one (Jeff, 2026-10-10), so there is no link rule to tell it.
        var prompt = Prompt(Build(which));

        Assert.Contains(ContentPromptBuilder.CurrencyInstruction, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"link\"", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryLedePrompt))]
    public void EveryLedePromptStatesAWordRangeAndAParagraphFloor(string which)
    {
        // "2-3 paragraphs" is a count, and three one-sentence paragraphs satisfies it exactly.
        var prompt = Prompt(Build(which));

        Assert.Contains(ContentLengthTargets.LedeRangeLabel, prompt, StringComparison.Ordinal);
        Assert.Contains(
            $"no paragraph in it is shorter than {ContentLengthTargets.LedeParagraphMinWords} words",
            prompt,
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryLedePrompt))]
    public void EveryLedePromptCanActuallyEmitTheLengthItAsksFor(string which)
    {
        // The ceiling was 1,024 for a response carrying 3-4 paragraphs of run-wrapped JSON. Asking
        // for length the budget cannot hold is the same defect as not asking at all.
        var request = Build(which);

        Assert.True(
            request.MaxOutputTokens >= 2048,
            $"{which} lede allows only {request.MaxOutputTokens} output tokens for "
            + $"{ContentLengthTargets.LedeRangeLabel} words of JSON-wrapped prose.");
    }

    [Theory]
    [MemberData(nameof(EveryLedePrompt))]
    public void EveryLedePromptAsksForAHeading(string which)
    {
        // Inverted 2026-09-29. This asserted the opposite -- that no lede prompt asks for a heading
        // -- which was the 2026-09-23 decision. Jeff: "While you are correct normally lede paragraphs
        // have no heading, in this codebase they do." The lede is the page's first H2: PillarPrompts
        // says "Its lede IS its first H2", GccGenerateService stores it as `lede with { Tag = "h2" }`,
        // and its outline slot is a SectionSlot.Cover the writer names.
        //
        // The prompt had been contradicting itself for six days: this contract asked for no heading
        // while the pillar user block said "You write its heading." -- so whether a page shipped one
        // came down to whether the model volunteered a key nobody asked for, and BuildLedeSection
        // then threw it away regardless.
        var prompt = Prompt(Build(which));

        // Scoped to the lede's own shape: nested h3 children carry headings too, so a blanket search
        // proves nothing about the lede.
        var ledeShapeStart = prompt.IndexOf("\"ledeType\"", StringComparison.Ordinal);
        Assert.True(ledeShapeStart >= 0, "the lede contract is missing from the prompt entirely");
        var ledeShape = prompt[ledeShapeStart..Math.Min(ledeShapeStart + 600, prompt.Length)];

        Assert.Contains("\"heading\"", ledeShape, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryLedePrompt))]
    public void EveryLedePromptForbidsRestatingTheTitle(string which)
    {
        // The reason the heading was removed in 2026-09-23 rather than governed: the title prints
        // immediately above it, so a heading restating the title sets one thought twice -- Jeff's
        // "How Automated Data Entry & Processing Can Transform Your Business then Transform Your
        // Business with Automated Data Entry & Processing seem redundant". Asking for the heading
        // again without this rule brings that back.
        var prompt = Prompt(Build(which));

        Assert.Contains("must not restate the page title", prompt, StringComparison.Ordinal);
        // And it is held to the same craft rules as every other heading on the page, so the opening
        // is not the one place "Overview" survives.
        Assert.Contains("HEADINGS: write them for this page and no other", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlogBodyPromptAsksForAnActionNotAReflection()
    {
        // "End with a clear next-step CTA" produced the same non-ending every time: "If you're
        // considering automation, it may be beneficial to explore similar success stories..."
        // No action, no actor, no next step (Jeff: "which every sample has lacked").
        var builder = new ContentPromptBuilder();
        var request = builder.BuildStandaloneBlogBodyPrompt(Context(), Blog);
        var prompt = Prompt(request);

        Assert.Contains("CLOSING:", prompt, StringComparison.Ordinal);
        Assert.Contains("it may be beneficial to explore", prompt, StringComparison.Ordinal);
        Assert.Contains("the ending has failed", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlogTargetMatchesWhatTheSectionsAddUpTo()
    {
        // A document total nothing supports section by section is the number the model ignores.
        Assert.Equal(2_000, ContentLengthTargets.BlogTargetMinWords);
        Assert.Equal(2_700, ContentLengthTargets.BlogTargetMaxWords);
        Assert.True(
            ContentLengthTargets.BlogSectionMinWords * ContentLengthTargets.BlogSectionCountMin
                >= ContentLengthTargets.BlogTargetMinWords,
            "The per-section floor cannot reach the document target.");
    }

    [Fact]
    public void TheLedeTargetIsAboutThreeTimesWhatTwoOrThreeShortParagraphsProduced()
    {
        // Jeff: "Should be 3 x that length". A three-sentence opening lands near 80 words.
        Assert.True(ContentLengthTargets.LedeMinWords >= 240);
        Assert.True(ContentLengthTargets.LedeTargetMaxWords > ContentLengthTargets.LedeMinWords);
        Assert.True(ContentLengthTargets.LedeParagraphMinWords * 3 <= ContentLengthTargets.LedeTargetMaxWords);
    }
}
