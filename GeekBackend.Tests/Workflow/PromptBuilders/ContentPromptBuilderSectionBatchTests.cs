using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// A long-form body is written in batches, so each call is told which sections it owns and which
/// belong to the other calls.
///
/// <para>
/// The reason is arithmetic, not style: a section written to its own word target costs roughly
/// twice its words in tokens once every run is wrapped in the JSON contract, so one response cannot
/// hold a 1,800- or 3,000-word page. Asking for the whole page in one call capped it — a blog came
/// back at 1,199 words against an 1,800 floor and failed keyword density as a consequence
/// (Jeff, 2026-09-28).
/// </para>
///
/// <para>
/// Asserted on the rendered prompt, per this folder's standard. What the model then returns is a
/// model output and proves nothing about what it was asked for.
/// </para>
/// </summary>
public class ContentPromptBuilderSectionBatchTests
{
    private const string Anchor = "#consultationAppointment2xl";

    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "ai implementation",
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
        ConsultationAnchorHref: Anchor,
        ConsultationCtaLabel: "Schedule a Free Consultation");

    private static string Rendered(ChatCompletionRequest request) =>
        string.Join("\n", request.Messages.Select(m => m.Content));

    private static List<string> BlogOutline() =>
        ["Where the hours go", "What changes first", "Mapping the data", "What to do next"];

    private static string BlogBatch(int batchIndex, params string[] owned) =>
        Rendered(new ContentPromptBuilder().BuildStandaloneBlogBodyPrompt(
            Context(),
            new BlogMetadataDraft("Title", "Meta", ["ai"], BlogOutline()),
            sectionBatch: [.. owned.Select(SectionSlot.Assigned)],
            batchIndex: batchIndex));

    private static string PillarBatch(int batchIndex, params string[] owned)
    {
        var full = BlogOutline().Select(SectionSlot.Assigned).ToList();
        return Rendered(new ContentPromptBuilder().BuildArticleSectionBatchPrompt(
            Context(),
            new ArticleMetadataDraft("Title", "Meta", ["ai"], BlogOutline()),
            slots: [.. owned.Select(SectionSlot.Assigned)],
            fullOutline: full,
            isRegeneration: false,
            batchIndex: batchIndex));
    }

    private static string ToolBatch(int batchIndex, int skip, int take)
    {
        var app = new SoftwareApplicationDescriptor("Partner Widget", "A widget.");
        var full = ToolPrompts.Outline(Context(), app.Name);
        return Rendered(new ContentPromptBuilder().BuildToolBodyPrompt(
            Context(),
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            app,
            "partner-widget",
            outline: [.. full.Skip(skip).Take(take)],
            fullOutline: full,
            batchIndex: batchIndex));
    }

    [Fact]
    public void A_blog_batch_is_told_which_sections_are_its_own()
    {
        var prompt = BlogBatch(0, "Where the hours go", "What changes first");

        Assert.Contains("Write ONLY these sections", prompt, StringComparison.Ordinal);
        Assert.Contains("- Where the hours go", prompt, StringComparison.Ordinal);
        Assert.Contains("- What changes first", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blog_batch_is_shown_the_sections_the_other_calls_own()
    {
        // Without the rest of the plan a batch re-covers its neighbours' ground and writes a
        // conclusion for a page it cannot see continuing.
        var prompt = BlogBatch(0, "Where the hours go", "What changes first");

        Assert.Contains("THE REST OF THIS POST, written by other calls", prompt, StringComparison.Ordinal);
        Assert.Contains("- Mapping the data", prompt, StringComparison.Ordinal);
        Assert.Contains("- What to do next", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unbatched_blog_still_gets_its_whole_outline_as_advisory()
    {
        var prompt = Rendered(new ContentPromptBuilder().BuildStandaloneBlogBodyPrompt(
            Context(), new BlogMetadataDraft("Title", "Meta", ["ai"], BlogOutline())));

        Assert.Contains("Advisory section outline", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("THE REST OF THIS POST", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Write ONLY these sections", prompt, StringComparison.Ordinal);
    }

    public static TheoryData<string> EveryBatchedType() => new() { "pillar", "blog", "tool" };

    private static string FirstBatch(string type) => type switch
    {
        "pillar" => PillarBatch(0, "Where the hours go", "What changes first"),
        "blog" => BlogBatch(0, "Where the hours go", "What changes first"),
        "tool" => ToolBatch(batchIndex: 0, skip: 0, take: 2),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown body prompt"),
    };

    private static string LastBatch(string type) => type switch
    {
        "pillar" => PillarBatch(1, "What to do next"),
        "blog" => BlogBatch(1, "What to do next"),
        "tool" => ToolBatch(
            batchIndex: 1, skip: ToolPrompts.Outline(Context(), "Partner Widget").Count - 1, take: 1),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown body prompt"),
    };

    [Theory]
    [MemberData(nameof(EveryBatchedType))]
    public void Only_the_batch_that_owns_the_last_section_is_asked_to_close(string type)
    {
        // "The last section ends by asking for..." is unambiguous in a single call and ambiguous in
        // every batch of a page. Given to all of them it produces a closing per call -- three asks
        // and three sign-offs on one page -- which is a worse page than the one batching fixed.
        Assert.Contains("CLOSING:", LastBatch(type), StringComparison.Ordinal);
        Assert.DoesNotContain("CLOSING:", FirstBatch(type), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryBatchedType))]
    public void A_batch_that_does_not_close_is_told_the_page_continues_past_it(string type)
    {
        // Silence would leave the model to infer it, and a model given two sections and no word
        // about the rest writes a wrap-up for them.
        var prompt = FirstBatch(type);

        Assert.Contains("This call does not end the page", prompt, StringComparison.Ordinal);
        Assert.Contains("no call to action", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryBatchedType))]
    public void A_batch_is_given_its_own_share_of_the_word_floor_not_the_whole_pages(string type)
    {
        // The SEO block states the page's numbers. Handed unchanged to a call writing two of six
        // sections it asks for the entire floor in a third of the page -- so the call either
        // truncates trying or reads the number as unreachable and ignores it. Either way the page
        // comes in short, which is the failure batching exists to fix.
        var prompt = FirstBatch(type);

        Assert.Contains("your share is about", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("words is the floor, not the aim", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryBatchedType))]
    public void A_batch_is_given_its_own_share_of_the_keyword_count(string type)
    {
        // Same arithmetic on density: the page's whole count written into each of three batches is
        // three times the mentions in the same number of words.
        var prompt = FirstBatch(type);

        Assert.Contains("times across the finished page, so about", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryBatchedType))]
    public void Only_one_batch_is_asked_for_the_keyword_bearing_heading(string type)
    {
        // The scorer wants at least one H2 carrying the keyword and the outline rules cap it at
        // two, so asking every batch for one puts it in half the headings on a six-section page.
        Assert.Contains(
            $"at least one H2 contains \"{Context().TargetKeyword}\"",
            FirstBatch(type),
            StringComparison.Ordinal);
        Assert.Contains(
            "keyword-bearing H2 is written by another call",
            LastBatch(type),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_retry_tells_the_model_what_it_is_retrying()
    {
        // GenerateToolPageAsync sets EvidenceBlock on the context for its closing-CTA retry.
        // BuildToolBodyPrompt had no parameter for it and ToolPrompts.Body did not forward it, so
        // the retry re-sent a byte-identical prompt -- a second paid draft that could not differ
        // from the first. Pillar and Blog have always rendered theirs.
        var app = new SoftwareApplicationDescriptor("Partner Widget", "A widget.");
        var full = ToolPrompts.Outline(Context(), app.Name);
        var prompt = Rendered(new ContentPromptBuilder().BuildToolBodyPrompt(
            Context(),
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            app,
            "partner-widget",
            outline: full,
            fullOutline: full,
            evidenceBlock: "RETRY: the closing did not link the scheduler."));

        Assert.Contains("RETRY: the closing did not link the scheduler.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unbatched_body_owns_its_own_closing()
    {
        // Nothing above applies to a page written in one call: it owns every section, so it owns
        // the end of the page too.
        var prompt = Rendered(new ContentPromptBuilder().BuildStandaloneBlogBodyPrompt(
            Context(), new BlogMetadataDraft("Title", "Meta", ["ai"], BlogOutline())));

        Assert.Contains("CLOSING:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("This call does not end the page", prompt, StringComparison.Ordinal);
    }
}
