using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;
using Xunit;

namespace GeekBackend.Tests;

public class GccV2WriteOutlineRulesTests
{
    [Fact]
    public void Pillar_always_skips_first_outline_section_when_outline_is_non_empty()
    {
        var outline = new List<GccV2OutlineSection>
        {
            new("intro", "Understanding AI Marketing", "problem", []),
            new("next", "Implementation Patterns", "advance", []),
        };

        var start = GccV2WriteOutlineRules.FirstBodyOutlineIndex("Different Lede Title", outline, pillar: true);

        Assert.Equal(1, start);
    }

    [Fact]
    public void Blog_skips_first_outline_section_only_when_headings_match()
    {
        var outline = new List<GccV2OutlineSection>
        {
            new("intro", "Why Teams Struggle With Data Quality", "problem", []),
            new("next", "Practical Next Steps", "advance", []),
        };

        Assert.Equal(1, GccV2WriteOutlineRules.FirstBodyOutlineIndex("Why Teams Struggle With Data Quality", outline, pillar: false));
        Assert.Equal(0, GccV2WriteOutlineRules.FirstBodyOutlineIndex("A Creative Hook", outline, pillar: false));
    }

    [Fact]
    public void MergeLedeAndIntroduction_concatenates_when_headings_match()
    {
        var lede = new Section("h2", "Understanding AI Marketing", [new TextParagraph([new Run("Hook.")])], null, []);
        var intro = new Section(
            "h2",
            "Understanding AI Marketing:",
            [new TextParagraph([new Run("Scope paragraph.")])],
            null,
            [new Section("h3", "Who this is for", [], null, [])]);

        var merged = GccV2WriteOutlineRules.MergeLedeAndIntroduction(lede, intro);

        Assert.Equal(2, merged.Paragraphs.Count);
        Assert.Single(merged.Children);
    }

    [Fact]
    public void SkippedOutlineEntryForLede_returns_first_outline_when_body_skips_index_zero()
    {
        var outline = new List<GccV2OutlineSection>
        {
            new("intro", "Understanding AI Marketing", "problem", ["KPIs"]),
            new("next", "Implementation Patterns", "advance", []),
        };

        var skipped = GccV2WriteOutlineRules.SkippedOutlineEntryForLede(outline, bodyStart: 1);

        Assert.NotNull(skipped);
        Assert.Equal("problem", skipped!.Job);
        Assert.Single(skipped.HierarchyChildHeadings);
    }
}
