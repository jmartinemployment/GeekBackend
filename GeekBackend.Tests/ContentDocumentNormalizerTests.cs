using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekBackend.Tests;

/// <summary>
/// Regression cover for duplicated body copy and whole-element links.
/// Both defects are taken from real exports: ai-content-repurposing.html
/// had 9 duplicated blocks and 10 whole-element links; old ai-marketing-systems
/// had 20/45. See fix-duplicated-body-copy-and-whole-element-links.md.
/// </summary>
public class ContentDocumentNormalizerTests
{
    private static ContentDocument Doc(Section lede, params Section[] sections) =>
        new(lede, sections.ToList());

    private static Section Lede(string text = "lede") =>
        new("h2", string.Empty, [new TextParagraph([new Run(text)])], null, []);

    private static Section H2(string heading, IReadOnlyList<Paragraph> paragraphs, IReadOnlyList<Section> children) =>
        new("h2", heading, paragraphs, null, children);

    private static Section H3(string heading, params string[] paragraphTexts) =>
        new("h3", heading, paragraphTexts.Select(t => (Paragraph)new TextParagraph([new Run(t)])).ToList(), null, []);

    private static TextParagraph Para(string text, bool bold = false, string? href = null) =>
        new([new Run(text, Bold: bold, Href: href)]);

    private static TextParagraph ParaRuns(params Run[] runs) => new(runs.ToList());

    // ------------------------------------------------------------------
    // Defect 1 — duplicated copy
    // ------------------------------------------------------------------

    [Fact]
    public void Drops_parent_paragraph_that_duplicates_child_prose()
    {
        var childProse = "KPIs are important for measuring success in content repurposing.";
        var child = H3("Key Performance Indicators (KPIs)", childProse);
        var parent = H2("Measuring Success in Content Repurposing",
            [Para("Intro para that is unique."), Para(childProse)],
            [child]);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));

        var resultParent = normalized.Sections[0];
        // Parent keeps only the intro; duplicated prose removed.
        Assert.Single(resultParent.Paragraphs);
        var remaining = Assert.IsType<TextParagraph>(resultParent.Paragraphs[0]);
        Assert.Equal("Intro para that is unique.", string.Concat(remaining.Runs.Select(r => r.Text)));
        // Child untouched.
        var resultChild = Assert.Single(resultParent.Children);
        Assert.Equal(childProse, string.Concat(((TextParagraph)resultChild.Paragraphs[0]).Runs.Select(r => r.Text)));
    }

    [Fact]
    public void Drops_all_bold_pseudo_heading_matching_child_heading()
    {
        var heading = "Key Performance Indicators (KPIs)";
        var child = H3(heading, "Child prose.");
        var pseudoHeading = Para(heading, bold: true);
        var parent = H2("Measuring Success",
            [pseudoHeading, Para("Child prose duplicate that will also be dropped if it matches")],
            [child]);

        // Make second parent paragraph NOT duplicate so we can isolate pseudo-heading test
        var parent2 = H2("Measuring Success",
            [pseudoHeading, Para("Unique intro.")],
            [child]);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent2));
        var resultParent = normalized.Sections[0];
        Assert.Single(resultParent.Paragraphs);
        var remaining = Assert.IsType<TextParagraph>(resultParent.Paragraphs[0]);
        Assert.Equal("Unique intro.", string.Concat(remaining.Runs.Select(r => r.Text)));
    }

    [Fact]
    public void Does_not_drop_pseudo_heading_when_not_all_runs_bold()
    {
        var heading = "Key Performance Indicators (KPIs)";
        var child = H3(heading, "Child prose.");
        var notAllBold = ParaRuns(new Run("Key ", Bold: true), new Run("Performance Indicators (KPIs)", Bold: false));
        var parent = H2("Measuring Success", [notAllBold], [child]);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));
        Assert.Single(normalized.Sections[0].Paragraphs);
    }

    [Fact]
    public void Keeps_parent_paragraph_that_merely_resembles_child_prose()
    {
        var child = H3("KPIs", "KPIs are important.");
        var almostSame = "KPIs are important for measuring success.";
        var parent = H2("Measuring Success", [Para(almostSame)], [child]);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));
        Assert.Single(normalized.Sections[0].Paragraphs);
    }

    [Fact]
    public void Drops_list_item_that_duplicates_child_prose()
    {
        var childProse = "First item content.";
        var child = H3("Subsection", childProse);
        // Parent has a ListParagraph with one item duplicating child's prose
        var list = new ListParagraph(false, [[new Run(childProse)]]);
        var parent = H2("Parent", [list], [child]);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));
        Assert.Empty(normalized.Sections[0].Paragraphs);
    }

    [Fact]
    public void Normalizes_whitespace_when_comparing()
    {
        var child = H3("KPIs", "KPIs are   important.");
        var parent = H2("Parent", [Para("KPIs are important.")], [child]);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));
        // Both normalize to "KPIs are important." so parent duplicate should be dropped.
        Assert.Empty(normalized.Sections[0].Paragraphs);
    }

    [Fact]
    public void Handles_nested_descendants_bottom_up()
    {
        var grandchild = H3("Deep", "Deep prose that duplicates.");
        var child = new Section("h3", "Child", [Para("Deep prose that duplicates.")], null, [grandchild]);
        var parent = H2("Parent", [Para("Deep prose that duplicates.")], [child]);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));
        // Parent duplicate dropped; child still has its own copy? Child's paragraph duplicates grandchild, so child copy also dropped.
        Assert.Empty(normalized.Sections[0].Paragraphs);
        Assert.Empty(normalized.Sections[0].Children[0].Paragraphs);
        Assert.Single(normalized.Sections[0].Children[0].Children[0].Paragraphs);
    }

    // ------------------------------------------------------------------
    // Defect 2 — whole-element links
    // ------------------------------------------------------------------

    [Fact]
    public void Narrows_whole_element_link_in_list_item_to_tool_name()
    {
        var longText = "Reduced manual editing: With tools like Jasper AI, content teams can automate the rewriting process, ensuring consistency and reducing the time spent on manual edits.";
        Assert.True(longText.Length > 60);
        var run = new Run(longText, Href: "/tools/marketing/jasper-ai");
        var list = new ListParagraph(false, [[run]]);
        var parent = H2("Section", [list], []);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));

        var resultList = Assert.IsType<ListParagraph>(normalized.Sections[0].Paragraphs[0]);
        var item = Assert.Single(resultList.Items);
        // Should be split into 3 runs: before / "Jasper AI" / after
        Assert.Equal(3, item.Count);
        Assert.Null(item[0].Href);
        Assert.Contains("Reduced manual editing", item[0].Text);
        Assert.Equal("Jasper AI", item[1].Text);
        Assert.Equal("/tools/marketing/jasper-ai", item[1].Href);
        Assert.Null(item[2].Href);
        Assert.Contains("content teams can automate", item[2].Text);
        // Surrounding text preserved exactly.
        Assert.Equal(longText, string.Concat(item.Select(r => r.Text)));
    }

    [Fact]
    public void Narrows_whole_element_link_in_paragraph()
    {
        var longText = "Geek At Your Spot helps organizations use AI tools like Copy.ai to transform blog articles into engaging social media posts or detailed infographics. This process not only saves time but also broadens the content's reach and impact.";
        Assert.True(longText.Length > 60);
        var run = new Run(longText, Href: "/tools/marketing/copyai");
        var parent = H2("Section", [ParaRuns(run)], []);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));

        var resultPara = Assert.IsType<TextParagraph>(normalized.Sections[0].Paragraphs[0]);
        Assert.Equal(3, resultPara.Runs.Count);
        Assert.Null(resultPara.Runs[0].Href);
        Assert.Equal("Copy.ai", resultPara.Runs[1].Text);
        Assert.Equal("/tools/marketing/copyai", resultPara.Runs[1].Href);
        Assert.Null(resultPara.Runs[2].Href);
        Assert.Equal(longText, string.Concat(resultPara.Runs.Select(r => r.Text)));
    }

    [Fact]
    public void Leaves_over_long_anchor_untouched_when_tool_name_not_found()
    {
        var longText = "This is a very long paragraph that does not contain the tool name at all and is definitely over sixty characters long for testing purposes.";
        Assert.True(longText.Length > 60);
        var run = new Run(longText, Href: "/tools/marketing/jasper-ai");
        var parent = H2("Section", [ParaRuns(run)], []);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));

        var resultPara = Assert.IsType<TextParagraph>(normalized.Sections[0].Paragraphs[0]);
        Assert.Single(resultPara.Runs);
        Assert.Equal(longText, resultPara.Runs[0].Text);
        Assert.Equal("/tools/marketing/jasper-ai", resultPara.Runs[0].Href);
    }

    [Fact]
    public void Leaves_short_anchor_untouched()
    {
        var shortText = "Jasper AI";
        Assert.True(shortText.Length <= 60);
        var run = new Run(shortText, Href: "/tools/marketing/jasper-ai");
        var parent = H2("Section", [ParaRuns(run)], []);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));

        var resultPara = Assert.IsType<TextParagraph>(normalized.Sections[0].Paragraphs[0]);
        Assert.Single(resultPara.Runs);
        Assert.Equal(shortText, resultPara.Runs[0].Text);
        Assert.Equal("/tools/marketing/jasper-ai", resultPara.Runs[0].Href);
    }

    [Fact]
    public void Leaves_exactly_sixty_char_anchor_untouched()
    {
        var text = new string('a', 60);
        Assert.Equal(60, text.Length);
        var run = new Run(text, Href: "/tools/marketing/jasper-ai");
        var parent = H2("Section", [ParaRuns(run)], []);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));

        var resultPara = Assert.IsType<TextParagraph>(normalized.Sections[0].Paragraphs[0]);
        Assert.Single(resultPara.Runs);
    }

    [Fact]
    public void Handles_multiple_runs_only_narrowing_over_long_linked_one()
    {
        var shortRun = new Run("Short text ", Href: "/tools/marketing/short");
        var longText = "Reduced manual editing: With tools like Jasper AI, content teams can automate the rewriting process, ensuring consistency.";
        Assert.True(longText.Length > 60);
        var longRun = new Run(longText, Href: "/tools/marketing/jasper-ai");
        var parent = H2("Section", [ParaRuns(shortRun, longRun)], []);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));

        var resultPara = Assert.IsType<TextParagraph>(normalized.Sections[0].Paragraphs[0]);
        // shortRun unchanged (length <=60 or no split), longRun split into 3 => total 4 runs
        Assert.Equal(4, resultPara.Runs.Count);
        Assert.Equal(shortRun.Text, resultPara.Runs[0].Text);
        Assert.Equal(shortRun.Href, resultPara.Runs[0].Href);
    }

    [Fact]
    public void Preserves_non_link_runs_unchanged()
    {
        var para = ParaRuns(new Run("Plain text without link."), new Run(" Another plain run."));
        var parent = H2("Section", [para], []);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));

        var resultPara = Assert.IsType<TextParagraph>(normalized.Sections[0].Paragraphs[0]);
        Assert.Equal(2, resultPara.Runs.Count);
    }

    [Fact]
    public void Handles_tool_name_with_hyphen_and_spacing_variants()
    {
        // SlugHelper turns spaces into hyphens, so "Copy AI" matches "copy-ai"
        var longText = "We use AI tools like Copy AI to generate content and it is a very long sentence that definitely exceeds sixty characters in total length.";
        Assert.True(longText.Length > 60);
        var run = new Run(longText, Href: "/tools/marketing/copy-ai");
        var parent = H2("Section", [ParaRuns(run)], []);

        var normalized = ContentDocumentNormalizer.Normalize(Doc(Lede(), parent));
        var resultPara = Assert.IsType<TextParagraph>(normalized.Sections[0].Paragraphs[0]);
        // Should have narrowed
        Assert.True(resultPara.Runs.Count > 1);
        var linked = resultPara.Runs.FirstOrDefault(r => r.Href != null);
        Assert.NotNull(linked);
        Assert.Equal("Copy AI", linked!.Text);
    }
}
