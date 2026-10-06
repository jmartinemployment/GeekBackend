using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// No pillar or blog section whose job is to list tools.
///
/// <para>
/// Reported three times, most recently as a provenance failure: "Top Tools for Automated Data Entry
/// &amp; Processing" (h2) with one h3 per product under it, each tagged
/// site:"Top 5 Automated Data Entry Processing Tools" -- a real heading on the site, which is not
/// one of the must-mention subtopics. The refusal was right and unreadable: the problem was never
/// the tag, it was the section.
/// </para>
/// </summary>
public class GccToolsSectionGuardTests
{
    private static Section H2(string heading, params Section[] children) =>
        new("h2", heading, [], null, [.. children]);

    private static Section H3(string heading) => new("h3", heading, [], null, []);

    [Theory]
    [InlineData("Top Tools for Automated Data Entry & Processing")]
    [InlineData("Choosing the Right AI Tools for Your Accounts Payable Needs")]
    [InlineData("The Best Tool for the Job")]
    public void A_tools_listing_h2_is_found(string heading)
    {
        Assert.Single(GccToolsSectionGuard.FindToolsSections([H2(heading)]));
    }


    [Fact]
    public void The_same_advisory_heading_over_a_product_list_is_still_found()
    {
        var section = H2(
            "Choosing the Right AI Tools for Your Accounts Payable Needs",
            H3("Melio: Simplify Payments"),
            H3("Dext: Accurate Data Capture"),
            H3("Lightyear: Smart Invoice Processing"));

        Assert.Single(GccToolsSectionGuard.FindToolsSections([section]));
    }

    [Fact]
    public void The_exact_shape_reported_is_found()
    {
        var sections = new[]
        {
            H2("Where the hours go"),
            H2("Top Tools for Automated Data Entry & Processing",
                H3("Melio: Simplify Payments"),
                H3("Dext: Accurate Data Capture"),
                H3("Lightyear: Smart Invoice Processing")),
        };

        Assert.Equal(["Top Tools for Automated Data Entry & Processing"],
            GccToolsSectionGuard.FindToolsSections(sections));
    }

    [Fact]
    public void A_product_named_subheading_under_an_ordinary_section_is_left_alone()
    {
        // A tools section is built from these, but on its own an h3 named for a product, under a
        // section about something else, is ordinary writing. Refusing it would block valid work.
        var sections = new[] { H2("How approvals route", H3("Melio: Simplify Payments")) };

        Assert.Empty(GccToolsSectionGuard.FindToolsSections(sections));
    }

    [Theory]
    [InlineData("Common Challenges and Solutions")]
    [InlineData("Measuring Success")]
    [InlineData("Where the hours go")]
    public void An_ordinary_section_is_not_flagged(string heading)
    {
        Assert.Empty(GccToolsSectionGuard.FindToolsSections([H2(heading)]));
    }

    [Fact]
    public void A_nested_tools_section_is_found_too()
    {
        var sections = new[] { H2("Implementation", new Section("h2", "Top Tools to Consider", [], null, [])) };

        Assert.Single(GccToolsSectionGuard.FindToolsSections(sections));
    }
}
