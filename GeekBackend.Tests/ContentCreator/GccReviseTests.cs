using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Revising a draft with its own type's prompt, carrying the feedback.
///
/// <para>
/// Jeff, 2026-09-28: pressed "Fix these and revise" three times and lost word count each time. One
/// cause was that revise wrote every type with the standalone blog prompt -- blog targets
/// 2,000-2,700 words, pillar and tool 3,500-5,000 -- so a tool page was handed a target a third
/// smaller than the draft it was revising, on every press.
/// </para>
///
/// <para>
/// The other was the lede: the first returned section was promoted into it and dropped from the
/// body, so the body lost a section per press. That is covered by
/// <see cref="ContentDocument"/> assembly in ReviseAsync rather than here; these pin the half that
/// is reachable without a provider -- that each type's set carries the feedback into its own prompt.
/// </para>
/// </summary>
public class GccReviseTests
{
    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "Automated Data Entry & Processing",
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
        Provider: LlmProviderType.OpenAi);

    /// <summary>
    /// Both metadata shapes, because Blog's prompts refuse the article one. ReviseAsync supplied
    /// only the article shape until this test found it, which meant revising a blog threw rather
    /// than revising.
    /// </summary>
    private static ContentTypePromptContext Ctx(string? notes) => new(
        Context(),
        Metadata: new ArticleMetadataDraft("Title", "Meta", ["ai"], ["One", "Two"]),
        BlogMetadata: new BlogMetadataDraft("Title", "Meta", ["ai"], ["One", "Two"]),
        Lede: new Section("h2", "Opening", [new TextParagraph([new Run("Invoices pile up.")])], null, []),
        RevisionNotes: notes);

    private static string Rendered(ChatCompletionRequest r) =>
        string.Join("\n", r.Messages.Select(m => m.Content));

    private static IContentTypePrompts SetFor(string key)
    {
        var builder = new ContentPromptBuilder();
        return key switch
        {
            "pillar" => new PillarPrompts(builder),
            "blog" => new BlogPrompts(builder),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "unknown set"),
        };
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void The_feedback_reaches_the_types_own_body_prompt(string key)
    {
        var rendered = Rendered(SetFor(key).Body(Ctx("Expand to at least 3,500 words.")));

        Assert.Contains("Expand to at least 3,500 words.", rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    public void Generation_carries_no_revision_notes(string key)
    {
        // The same set writes the first draft. Feedback is a revise-only concern, so a fresh
        // generation must not pick up a revision block from an unset field.
        var rendered = Rendered(SetFor(key).Body(Ctx(null)));

        Assert.DoesNotContain("REVISION", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_pillar_is_revised_as_a_pillar_not_as_a_blog()
    {
        // The measurable half of the bug: revise used the standalone blog prompt for every type, so
        // a pillar was rewritten under blog instructions and to blog length.
        var pillar = Rendered(SetFor("pillar").Body(Ctx("Expand it.")));
        var blog = Rendered(SetFor("blog").Body(Ctx("Expand it.")));

        Assert.Contains("pillar", pillar, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("standalone deep-dive blog", pillar, StringComparison.Ordinal);
        Assert.Contains("blog", blog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_draft_reaches_the_prompt_as_the_draft_and_not_as_site_copy()
    {
        // The flattened draft used to go into CrawledParagraphs, which the research brief renders
        // under "Representative site copy:" -- so the model was shown the previous draft labelled
        // as background from the publisher's website, with nothing saying it was the thing being
        // revised. It rewrote from what it had been told it was looking at.
        var notes = "=== THE DRAFT YOU ARE REVISING ===\n[H2] Where the hours go\nThree days of keying.\n\nExpand it.";
        var rendered = Rendered(SetFor("blog").Body(Ctx(notes)));

        Assert.Contains("THE DRAFT YOU ARE REVISING", rendered, StringComparison.Ordinal);
        Assert.Contains("Three days of keying.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void The_draft_block_carries_no_markdown()
    {
        // A prompt that shows the model Markdown gets Markdown back, and it is banned end to end.
        var notes = "=== THE DRAFT YOU ARE REVISING ===\n[H2] Where the hours go\nThree days of keying.";
        var rendered = Rendered(SetFor("blog").Body(Ctx(notes)));

        Assert.DoesNotContain("## Where the hours go", rendered, StringComparison.Ordinal);
        Assert.Contains("[H2] Where the hours go", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void The_body_prompt_is_told_the_opening_that_already_exists()
    {
        // Revise keeps the document's lede rather than promoting a body section into it, so the
        // body has to be written to follow that opening.
        var rendered = Rendered(SetFor("blog").Body(Ctx("Tighten it.")));

        Assert.Contains("Invoices pile up.", rendered, StringComparison.Ordinal);
    }
}
