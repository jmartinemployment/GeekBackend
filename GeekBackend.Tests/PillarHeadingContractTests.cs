using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekBackend.Tests;

/// <summary>
/// Regression cover for duplicate pillar H2s. Both cases below are taken from real exports:
/// the Automated Ad Spend Optimization pillar rendered "Data Quality Assessments:" and
/// "Data Quality Assessments" as two separate H2s, and the AI Content Creation Workflow pillar
/// rendered "AI Content Repurposing: Maximize Your Content's Reach" twice.
/// </summary>
public class PillarHeadingContractTests
{
    private static Section Body(string heading) =>
        new("h2", heading, [new TextParagraph([new Run("body")])], null, []);

    [Theory]
    [InlineData("Data Quality Assessments:", "data quality assessments")]
    [InlineData("Data Quality Assessments", "data quality assessments")]
    [InlineData("  Dynamic Creative   Optimization:  ", "dynamic creative optimization")]
    [InlineData("Automated Rules & Bidding:", "automated rules & bidding")]
    public void HeadingKey_ignores_trailing_punctuation_and_whitespace(string heading, string expected) =>
        Assert.Equal(expected, PillarHeadingContract.HeadingKey(heading));

    [Fact]
    public void HeadingKey_treats_colon_truncated_heading_as_the_same_section()
    {
        // The exact pair that shipped as two H2s in the Ad Spend export.
        Assert.Equal(
            PillarHeadingContract.HeadingKey("Data Quality Assessments:"),
            PillarHeadingContract.HeadingKey("Data Quality Assessments"));
    }

    [Fact]
    public void HeadingKey_keeps_genuinely_different_sections_apart()
    {
        Assert.NotEqual(
            PillarHeadingContract.HeadingKey("Data Quality Assessments"),
            PillarHeadingContract.HeadingKey("Data Quality Assessments: Tools for Validation"));
    }

    [Fact]
    public void FindDuplicateOutlineHeadings_is_empty_for_a_well_formed_outline()
    {
        string[] outline =
        [
            "Unlocking Automated Ad Spend Optimization",
            "Dynamic Creative Optimization: Personalization at Scale",
            "Automated Rules & Bidding: Smarter Spend Control",
            "People Also Ask",
        ];

        Assert.Empty(PillarHeadingContract.FindDuplicateOutlineHeadings(outline));
    }

    [Fact]
    public void FindDuplicateOutlineHeadings_reports_the_colliding_spellings()
    {
        string[] outline =
        [
            "Dynamic Creative Optimization",
            "Data Quality Assessments:",
            "Data Quality Assessments",
        ];

        var duplicates = PillarHeadingContract.FindDuplicateOutlineHeadings(outline);

        Assert.Single(duplicates);
        Assert.Contains("Data Quality Assessments:", duplicates[0]);
        Assert.Contains("Data Quality Assessments", duplicates[0]);
    }

    [Fact]
    public void FindDuplicateOutlineHeadings_ignores_blank_entries()
    {
        string[] outline = ["Real Section", "  ", "", "Another Section"];
        Assert.Empty(PillarHeadingContract.FindDuplicateOutlineHeadings(outline));
    }

    [Fact]
    public void WithPlannedHeading_restores_a_heading_the_model_truncated()
    {
        var planned = "Data Quality Assessments: Ensuring Data Integrity";
        var generated = Body("Data Quality Assessments:");

        var bound = PillarHeadingContract.WithPlannedHeading(generated, planned);

        Assert.Equal(planned, bound.Heading);
    }

    [Fact]
    public void WithPlannedHeading_keeps_paragraphs_and_children_intact()
    {
        var child = new Section("h3", "Automated Data Monitoring", [], null, []);
        var generated = new Section("h2", "Data Quality Assessments:",
            [new TextParagraph([new Run("kept")])], null, [child]);

        var bound = PillarHeadingContract.WithPlannedHeading(generated, "Data Quality Assessments: Ensuring Data Integrity");

        Assert.Same(generated.Paragraphs, bound.Paragraphs);
        Assert.Same(generated.Children, bound.Children);
        Assert.Equal("h2", bound.Tag);
    }

    [Fact]
    public void WithPlannedHeading_returns_the_section_unchanged_when_the_model_obeyed()
    {
        var generated = Body("Dynamic Creative Optimization: Personalization at Scale");

        var bound = PillarHeadingContract.WithPlannedHeading(
            generated, "Dynamic Creative Optimization: Personalization at Scale");

        Assert.Same(generated, bound);
    }

    [Fact]
    public void WithPlannedHeading_leaves_the_model_heading_when_no_heading_was_planned()
    {
        var generated = Body("Whatever The Model Chose");
        Assert.Same(generated, PillarHeadingContract.WithPlannedHeading(generated, "   "));
    }

    [Fact]
    public void Two_distinct_planned_sections_no_longer_collide_after_binding()
    {
        // The Ad Spend failure end to end: distinct plan entries, both truncated by the model to
        // the same string. Binding restores the planned text, so the rendered H2s differ again.
        var planned = new[]
        {
            "Data Quality Assessments: Ensuring Data Integrity",
            "Data Quality Assessments: Tools for Validation",
        };
        var asGenerated = new[] { Body("Data Quality Assessments:"), Body("Data Quality Assessments") };

        Assert.Equal(
            PillarHeadingContract.HeadingKey(asGenerated[0].Heading),
            PillarHeadingContract.HeadingKey(asGenerated[1].Heading));

        var bound = asGenerated.Select((s, i) => PillarHeadingContract.WithPlannedHeading(s, planned[i])).ToList();

        Assert.NotEqual(
            PillarHeadingContract.HeadingKey(bound[0].Heading),
            PillarHeadingContract.HeadingKey(bound[1].Heading));
    }
}

public sealed class PillarPlanViolationTests
{
    [Theory]
    [InlineData("Top AI Content Creation Tools")]
    [InlineData("Top 5 Automated Data Entry Processing Tools:")]
    [InlineData("Tools")]
    [InlineData("The Right Tool for the Job")]
    public void A_tools_h2_makes_the_plan_unwritable(string heading)
    {
        var violations = PillarHeadingContract.FindPlanViolations(
            ["Opening", heading, "Benefits of AI Marketing"]);

        Assert.Contains(violations, v => v.Contains("Tools H2", StringComparison.Ordinal));
    }

    [Theory]
    // Rejecting a plan is destructive to the user's work, so the predicate must not fire on
    // ordinary section titles. "Solutions" in particular tripped the loose classifier.
    [InlineData("Common Challenges and Solutions")]
    [InlineData("Choosing the Right Platform")]
    [InlineData("Vendor Selection Criteria")]
    [InlineData("Building Your Technology Stack")]
    [InlineData("Software Buying Considerations")]
    public void Ordinary_headings_are_not_treated_as_a_tools_listing(string heading)
    {
        var violations = PillarHeadingContract.FindPlanViolations(["Opening", heading]);
        Assert.Empty(violations);
    }

    [Fact]
    public void A_clean_outline_has_no_violations()
    {
        var violations = PillarHeadingContract.FindPlanViolations(
            ["Opening", "Benefits of AI Marketing", "Implementation Steps"]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Duplicates_and_tools_are_reported_together()
    {
        var violations = PillarHeadingContract.FindPlanViolations(
            ["Benefits", "Benefits:", "Top AI Marketing Tools"]);

        Assert.Equal(2, violations.Count);
    }
}
