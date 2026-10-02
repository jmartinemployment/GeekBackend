using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// One partner's share of a create's grounding — the unit both the set-of-five and the single tool page
/// are built from (Jeff, 2026-10-02).
/// </summary>
public class GccPartnerToolSlicesTests
{
    private static readonly string[] PartnerUrls =
        ["https://dext.com", "https://www.bill.com/", "https://melio.com"];

    private static GccQuoteablePage Page(string url, string text) =>
        new(url, "A page", [], [text]);

    private static GccGroundedPassage Passage(string url, string text) =>
        new(url, "A page", [new TextParagraph([new Run(text)])]);

    private static GccCreateDto Create(params GccQuoteablePage[] quoteables) => new(
        Id: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        OwnerUserId: Guid.NewGuid(),
        StartingContentType: "tool",
        Topic: "Accounts Payable: Automated Data Entry & Processing",
        Notes: "notes",
        ProjectSiteRunId: null,
        SiteSectionJson: null,
        BriefJson: null,
        ResearchJson: GccResearchFetchService.Serialize(
            new GccResearchDocument(null, [.. quoteables])),
        Status: "draft",
        CreatedAtUtc: DateTime.UtcNow,
        UpdatedAtUtc: DateTime.UtcNow,
        Department: "accounting",
        ProjectId: Guid.NewGuid());

    [Fact]
    public void One_slice_per_declared_partner_in_declared_order()
    {
        var create = Create(
            Page("https://bill.com/features", "Bill.com routes approvals."),
            Page("https://dext.com/pricing", "Dext costs this much."));

        var slices = GccPartnerToolSlices.Build(create, PartnerUrls, []);

        Assert.Equal(["dext.com", "bill.com", "melio.com"], slices.Select(s => s.Host));
    }

    [Fact]
    public void Each_slice_carries_only_its_own_partners_pages()
    {
        var create = Create(
            Page("https://dext.com/a", "Dext A"),
            Page("https://www.dext.com/b", "Dext B"),
            Page("https://bill.com/c", "Bill C"));

        var slices = GccPartnerToolSlices.Build(create, PartnerUrls, []);

        var dext = Assert.Single(slices, s => s.Host == "dext.com");
        // www. and bare host are the same partner -- the bucket key strips it, as the name lookup does.
        Assert.Equal(2, dext.Pages.Count);
        Assert.All(dext.Pages, p => Assert.Contains("dext.com", p.Url, StringComparison.Ordinal));

        var bill = Assert.Single(slices, s => s.Host == "bill.com");
        Assert.Single(bill.Pages);
    }

    [Fact]
    public void The_product_name_is_the_subject_not_the_topic()
    {
        // The whole point. Topic is the problem; the product is what the page is about. Host-derived
        // when the brief carries no row, which AnchorLookup already owns.
        var slices = GccPartnerToolSlices.Build(Create(), PartnerUrls, []);

        Assert.Equal(["Dext", "Bill", "Melio"], slices.Select(s => s.ProductName));
        Assert.All(slices, s => Assert.DoesNotContain("Accounts Payable", s.ProductName, StringComparison.Ordinal));
    }

    [Fact]
    public void A_partner_with_no_retrieved_pages_still_gets_a_slice()
    {
        // It will refuse its own page at the sufficiency gate, by name. Dropping it here would make a
        // declared partner vanish with no error.
        var create = Create(Page("https://dext.com/a", "Dext A"));

        var slices = GccPartnerToolSlices.Build(create, PartnerUrls, []);

        Assert.Equal(3, slices.Count);
        var melio = Assert.Single(slices, s => s.Host == "melio.com");
        Assert.Empty(melio.Pages);
        Assert.Equal("Melio", melio.ProductName);
    }

    [Fact]
    public void Passages_are_bucketed_by_the_same_key_as_pages()
    {
        var create = Create(Page("https://dext.com/a", "Dext A"), Page("https://bill.com/c", "Bill C"));

        var slices = GccPartnerToolSlices.Build(
            create,
            PartnerUrls,
            [Passage("https://dext.com/a", "Dext says this."), Passage("https://bill.com/c", "Bill says that.")]);

        var dext = Assert.Single(slices, s => s.Host == "dext.com");
        Assert.Single(dext.Passages);
        Assert.Contains("Dext says this.", dext.Passages[0].Content.OfType<TextParagraph>()
            .SelectMany(tp => tp.Runs).Select(r => r.Text));
    }

    [Fact]
    public void Narrow_hands_the_page_a_create_that_sees_only_its_partner()
    {
        var create = Create(Page("https://dext.com/a", "Dext A"), Page("https://bill.com/c", "Bill C"));
        var dext = Assert.Single(GccPartnerToolSlices.Build(create, PartnerUrls, []), s => s.Host == "dext.com");

        var narrowed = dext.Narrow(create);

        var quoteables = GccResearchFetchService.Deserialize(narrowed.ResearchJson)?.Quoteables ?? [];
        Assert.Single(quoteables);
        Assert.Equal("https://dext.com/a", quoteables[0].Url);
        // The multi-origin ambiguity check in GenerateToolPageAsync can now pass instead of blocking.
        Assert.Single(quoteables.Select(q => new Uri(q.Url).GetLeftPart(UriPartial.Authority)).Distinct());
    }

    [Fact]
    public void ForProduct_returns_that_partners_slice_and_nothing_else()
    {
        var create = Create(Page("https://dext.com/a", "Dext A"), Page("https://bill.com/c", "Bill C"));

        var slice = GccPartnerToolSlices.ForProduct(create, PartnerUrls, [], "Dext");

        Assert.NotNull(slice);
        Assert.Equal("dext.com", slice!.Host);
        Assert.Single(slice.Pages);
    }

    [Theory]
    [InlineData("Notion")]
    [InlineData("")]
    [InlineData("   ")]
    public void ForProduct_is_null_for_a_product_this_project_declared_no_partner_for(string name)
    {
        // Null rather than a guess: a page for an undeclared product has no evidence to be grounded in.
        Assert.Null(GccPartnerToolSlices.ForProduct(Create(), PartnerUrls, [], name));
    }

    [Fact]
    public void No_declared_partners_is_no_slices()
    {
        Assert.Empty(GccPartnerToolSlices.Build(Create(), [], []));
    }
}
