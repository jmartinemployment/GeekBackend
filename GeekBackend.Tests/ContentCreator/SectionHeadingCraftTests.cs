using GeekAPI.Services.ContentCreator.ContentTypes;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Headings, section variety and the opening the body continues -- one defect reported in four
/// parts by Jeff on 2026-09-23:
///
/// "Headings are lame and I would bet repeated on every single blog post" ·
/// "The headings reflect why content word count is so drastically low" ·
/// "While it starts off nice with a story, it becomes dull and a chore to read afterward" ·
/// "I really don't want to see Overview again, on any content type. Overview is a type of Lede."
///
/// The bet was safe by construction: Pillar and Tool outlines were compile-time heading lists, so
/// every page of a type shipped byte-identical H2s. These tests assert the outlines are obligations
/// now, that no type can produce an "Overview" section, and that the body call is actually shown
/// the opening it is supposed to continue.
/// </summary>
public class SectionHeadingCraftTests
{
    private static ProjectGenerationContext Context(string? angle = null) => new(
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
        Provider: GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi)
    {
        ContentAngle = angle,
    };

    private static readonly string[] BannedHeadings =
    [
        "overview", "introduction", "key capabilities", "key considerations", "key benefits",
        "key takeaways", "how it works", "why it matters", "common challenges", "best practices",
        "getting started", "next steps", "final thoughts", "conclusion", "when to use",
        "evaluation criteria", "implementation considerations",
    ];

    [Theory]
    [InlineData("problem_solution")]
    [InlineData("comparative")]
    [InlineData("case_study_data")]
    [InlineData("ultimate_guide")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("an_angle_nobody_has_defined_yet")]
    public void NoToolOutlineSlotIsAReusableHeadingForAnyAngle(string? angle)
    {
        // "ultimate_guide" and the unrecognised/unset cases both used to return the literal
        // "Overview" -- the fallback was the most common path of all.
        var outline = ToolPrompts.Outline(Context(angle), "Partner Widget");

        Assert.NotEmpty(outline);
        foreach (var slot in outline)
        {
            Assert.True(slot.WritesItsOwnHeading, $"\"{slot.Label}\" is a fixed heading, not an obligation.");
            Assert.DoesNotContain(BannedHeadings, banned =>
                slot.Label.Trim().Equals(banned, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheToolOutlineNamesTheProductSoTwoToolsCannotShareOne()
    {
        var first = ToolPrompts.Outline(Context("problem_solution"), "Partner Widget");
        var second = ToolPrompts.Outline(Context("problem_solution"), "Other Platform");

        Assert.Contains(first, s => s.Label.Contains("Partner Widget", StringComparison.Ordinal));
        Assert.DoesNotContain(second, s => s.Label.Contains("Partner Widget", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePillarOutlineIsObligationsNotTitles()
    {
        var pillar = new PillarPrompts(new ContentPromptBuilder());

        var outline = pillar.OutlineFor(new ContentTypePromptContext(Context()));

        Assert.Equal(6, outline.Count);
        Assert.All(outline, slot => Assert.True(slot.WritesItsOwnHeading));
        Assert.DoesNotContain(outline, slot => BannedHeadings.Contains(slot.Label.Trim().ToLowerInvariant()));
    }

    [Fact]
    public void ThePillarBodyPromptTellsTheModelToWriteItsOwnHeadingsAndWhichOnesAreBanned()
    {
        var pillar = new PillarPrompts(new ContentPromptBuilder());
        var metadata = new ArticleMetadataDraft("Title", "Meta", ["ai"], ["a", "b"]);

        var request = pillar.Body(new ContentTypePromptContext(Context(), Metadata: metadata));
        var system = string.Join("\n", request.Messages.Where(m => m.Role == ChatRole.System).Select(m => m.Content));
        var user = string.Join("\n", request.Messages.Where(m => m.Role == ChatRole.User).Select(m => m.Content));

        Assert.Contains("HEADINGS: write them for this page and no other", system, StringComparison.Ordinal);
        Assert.Contains("an overview is a kind of lede", system, StringComparison.Ordinal);
        // The stock openers are unfinished, not forbidden. A blacklist would reject "How Invoice
        // Capture Actually Works" for containing "How It Works" -- forbidding a construction that
        // is right the moment it carries the page's own subject (Jeff, 2026-09-23: "Add a word or
        // two to any of those 16 banned and they work much better...?").
        Assert.Contains("are not forbidden, they are unfinished", system, StringComparison.Ordinal);
        Assert.Contains("\"How Invoice Capture Actually Works\" is a heading", system, StringComparison.Ordinal);
        Assert.Contains("VARY THE SECTIONS", system, StringComparison.Ordinal);
        Assert.Contains("you write its heading", user, StringComparison.Ordinal);
    }

    [Fact]
    public void AssignedSlotsDoNotAskTheModelToRenameHeadingsSomethingElseAlreadyPlanned()
    {
        // The orchestrator plans its outline per keyword, so those headings are already the page's
        // own. Only the Create path's constants needed replacing -- telling the plan path to rewrite
        // what it just planned would be two callers disagreeing about who names a section.
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", ["ai"], ["Planned One", "Planned Two"]);

        var request = builder.BuildArticleSectionBatchPrompt(
            Context(), metadata,
            slots: [SectionSlot.Assigned("Planned Two")],
            fullOutline: [SectionSlot.Assigned("Planned One"), SectionSlot.Assigned("Planned Two")],
            isRegeneration: false);

        var system = string.Join("\n", request.Messages.Where(m => m.Role == ChatRole.System).Select(m => m.Content));
        Assert.DoesNotContain("HEADINGS: write them for this page and no other", system, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBodyPromptIsShownTheOpeningItHasToContinue()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", ["ai"], ["a"]);
        var lede = new Section(
            "h2",
            "The invoice that sat in a drawer for nine days",
            [new TextParagraph([new Run("It was still there on Friday.")])],
            null,
            []);

        var request = builder.BuildArticleSectionBatchPrompt(
            Context(), metadata,
            slots: [SectionSlot.Cover("what the delay costs")],
            fullOutline: [SectionSlot.Cover("what the delay costs")],
            isRegeneration: false,
            lede: lede);

        var system = string.Join("\n", request.Messages.Where(m => m.Role == ChatRole.System).Select(m => m.Content));
        Assert.Contains("THE OPENING THIS PAGE ALREADY HAS", system, StringComparison.Ordinal);
        Assert.Contains("The invoice that sat in a drawer for nine days", system, StringComparison.Ordinal);
        Assert.Contains("It was still there on Friday.", system, StringComparison.Ordinal);
        Assert.Contains("do not drop into neutral textbook voice", system, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoLedeThereIsNoContinuityBlockRatherThanAnEmptyOne()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", ["ai"], ["a"]);

        var request = builder.BuildArticleSectionBatchPrompt(
            Context(), metadata,
            slots: [SectionSlot.Cover("what the delay costs")],
            fullOutline: [SectionSlot.Cover("what the delay costs")],
            isRegeneration: false);

        var system = string.Join("\n", request.Messages.Where(m => m.Role == ChatRole.System).Select(m => m.Content));
        Assert.DoesNotContain("THE OPENING THIS PAGE ALREADY HAS", system, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlogBodyPromptCarriesAPerSectionBudgetNotJustADocumentTotal()
    {
        // 5-6 sections against a document total of 1,800 is a number the model cannot act on while
        // writing section three. Blog carried only the total and came back at 791 words; these two
        // constants already existed and nothing on this path used them.
        var builder = new ContentPromptBuilder();
        var metadata = new BlogMetadataDraft("Title", "Meta", ["ai"], ["a", "b"]);

        var request = builder.BuildStandaloneBlogBodyPrompt(Context(), metadata);
        var system = string.Join("\n", request.Messages.Where(m => m.Role == ChatRole.System).Select(m => m.Content));

        Assert.Contains(
            $"Each section runs {ContentLengthTargets.BlogSectionMinWords}-{ContentLengthTargets.BlogSectionTargetMaxWords} words",
            system,
            StringComparison.Ordinal);
        Assert.Contains("HEADINGS: write them for this page and no other", system, StringComparison.Ordinal);
    }

    [Fact]
    public void TheToolBodyPromptRendersTheOutlineFromItsOneDefinition()
    {
        // The required-sections list, the per-section word budget and the per-section guidance were
        // three hand-written prose blocks inside this prompt, beside a fourth copy in ToolPrompts
        // and a fifth in GccGenerateService, under a comment saying they had to be kept in sync.
        var builder = new ContentPromptBuilder();
        var context = Context("problem_solution");
        var app = new GeekAPI.Services.Workflow.Services.SchemaBuilders.SoftwareApplicationDescriptor(
            "Partner Widget", "A widget.");
        var outline = ToolPrompts.Outline(context, app.Name);

        var request = builder.BuildToolBodyPrompt(
            context, new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []), app, "partner-widget", outline);
        var system = string.Join("\n", request.Messages.Where(m => m.Role == ChatRole.System).Select(m => m.Content));

        Assert.Contains($"Write {outline.Count} top-level (h2) sections", system, StringComparison.Ordinal);
        Assert.All(outline, slot => Assert.Contains(slot.Label, system, StringComparison.Ordinal));
        Assert.DoesNotContain("Required top-level (h2) sections, in order: Overview", system, StringComparison.Ordinal);
    }
}
