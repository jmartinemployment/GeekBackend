using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A section whose JOB is to list tools — which the heading alone cannot tell you.
///
/// <para>
/// This guard rejected on <c>\btools?\b</c>: any h2 containing the word. It refused "How AI Tools
/// Simplify Your Accounts Payable Process" and "Choosing the Right AI Tool for Your Business
/// Needs", neither of which lists anything, and the retry could not rescue them because every
/// honest heading for that material contains the word.
/// </para>
/// </summary>
public class GccToolsSectionStructureTests
{
    private static Section H2(string heading, params Section[] children) =>
        new("h2", heading, [], null, children);

    private static Section H3(string heading) => new("h3", heading, [], null, []);

    private static IReadOnlyList<string> Found(params Section[] sections) =>
        GccToolsSectionGuard.FindToolsSections(sections);

    [Theory]
    [InlineData("How AI Tools Simplify Your Accounts Payable Process")]
    [InlineData("Choosing the Right AI Tool for Your Business Needs")]
    [InlineData("Why Your Current Tool Is Costing You Money")]
    [InlineData("Using Automation Tools Without Replacing Your ERP")]
    [InlineData("What Does an AP Tool Actually Do?")]
    public void Prose_about_tools_is_not_a_tools_section(string heading)
    {
        // The live refusals. Prose that mentions tools is exactly what the retry instruction asks
        // the writer to produce, so rejecting it leaves nowhere to go.
        Assert.Empty(Found(H2(heading)));
    }

    [Theory]
    [InlineData("Top 5 AP Automation Tools")]
    [InlineData("The Best Tools for Accounts Payable")]
    [InlineData("7 Tools to Consider This Year")]
    [InlineData("AP Tools Compared")]
    [InlineData("Leading Tools for Invoice Capture")]
    public void An_enumerating_heading_is_a_tools_section(string heading)
    {
        Assert.Single(Found(H2(heading)));
    }

    [Fact]
    public void A_section_built_as_a_list_of_products_is_a_tools_section()
    {
        // The shape the ban exists for, and the one the heading hides: an unassuming h2 with one
        // product per child underneath it.
        var section = H2(
            "AI Tools for Accounts Payable",
            H3("Tipalti"), H3("Stampli"), H3("Bill.com"), H3("AvidXchange"));

        Assert.Single(Found(section));
    }

    [Fact]
    public void Prose_subsections_under_a_tools_heading_are_not_a_list()
    {
        var section = H2(
            "How AI Tools Simplify Accounts Payable",
            H3("Why manual capture is slow"),
            H3("What changes when matching is automated"),
            H3("Where approvals still need a human"));

        Assert.Empty(Found(section));
    }

    [Fact]
    public void Two_product_children_is_structure_not_a_list()
    {
        // A list starts at three. Two sub-sections under a section about tools is ordinary writing.
        var section = H2("Working With AP Tools", H3("Tipalti"), H3("Stampli"));

        Assert.Empty(Found(section));
    }

    [Fact]
    public void A_heading_without_the_word_is_never_flagged_however_it_is_built()
    {
        // Widening to platform/solution/vendor is what IsToolsListingHeading's own doc warns
        // against -- it flagged "Common Challenges and Solutions" -- and this guard rejects a draft
        // outright, so it cannot afford that.
        var section = H2(
            "AP Automation Platforms",
            H3("Tipalti"), H3("Stampli"), H3("Bill.com"));

        Assert.Empty(Found(section));
    }

    [Fact]
    public void A_listing_nested_under_another_section_is_still_found()
    {
        var doc = H2("Automating Accounts Payable", H2("Top 5 AP Tools"));

        Assert.Single(Found(doc));
    }

    [Fact]
    public void An_h3_listing_is_not_the_shape_this_bans()
    {
        // Only the top-level shape is a "tools section" -- an h3 named for a product is how one is
        // built, but on its own it is ordinary writing.
        Assert.Empty(Found(new Section("h3", "Top 5 AP Tools", [], null, [])));
    }

    [Fact]
    public void Both_live_refusals_now_pass_together()
    {
        // The exact pair Generate refused on 2026-10-01.
        Assert.Empty(Found(
            H2("How AI Tools Simplify Your Accounts Payable Process"),
            H2("Choosing the Right AI Tool for Your Business Needs")));
    }
}
