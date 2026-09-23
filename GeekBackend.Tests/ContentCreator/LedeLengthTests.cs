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

    [Fact]
    public void TheLedeTargetIsAboutThreeTimesWhatTwoOrThreeShortParagraphsProduced()
    {
        // Jeff: "Should be 3 x that length". A three-sentence opening lands near 80 words.
        Assert.True(ContentLengthTargets.LedeMinWords >= 240);
        Assert.True(ContentLengthTargets.LedeTargetMaxWords > ContentLengthTargets.LedeMinWords);
        Assert.True(ContentLengthTargets.LedeParagraphMinWords * 3 <= ContentLengthTargets.LedeTargetMaxWords);
    }
}
