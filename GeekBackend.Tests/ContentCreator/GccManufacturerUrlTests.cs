using GeekAPI.Services.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A SoftwareApplication's url is where the product lives, and it is the manufacturer's.
///
/// <para>
/// Our page about it is mainEntityOfPage, a separate fact. Publishing ours as the product's url
/// told search engines "Tipalti is located at geekatyourspot.com" — a false claim about four real
/// companies, two of them partners (2026-09-23). That was fixed by passing null; this is the rest
/// of the fix, now that the project declares the partner URLs the vendor domain was missing from.
/// </para>
/// </summary>
public class GccManufacturerUrlTests
{
    private static readonly string[] Partners =
    [
        "https://tipalti.com/ap-automation/",
        "https://www.stampli.com/case-studies/cti/",
        "https://www.corpay.com",
    ];

    [Fact]
    public void A_declared_partner_resolves_to_its_own_home_page()
    {
        var byName = GccRequiredToolMentions.HomeUrlByName(null, Partners);

        Assert.Equal("https://tipalti.com", byName["Tipalti"]);
    }

    [Fact]
    public void A_deep_link_contributes_the_home_page_not_the_page_declared()
    {
        // A partner URL may be a deep link to one page. A product's url is its home, not whichever
        // page the operator happened to declare.
        var byName = GccRequiredToolMentions.HomeUrlByName(null, Partners);

        Assert.Equal("https://www.stampli.com", byName["Stampli"]);
        Assert.DoesNotContain("case-studies", byName["Stampli"], StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_that_is_not_a_declared_partner_has_no_url()
    {
        // Left unset rather than guessed. A wrong vendor domain attached to a product is a false
        // claim about a real company -- worse than the omission this replaces.
        var byName = GccRequiredToolMentions.HomeUrlByName(null, Partners);

        Assert.Null(byName.GetValueOrDefault("Some Tool We Never Declared"));
    }

    [Fact]
    public void No_declared_partners_means_no_urls_which_is_not_an_error()
    {
        Assert.Empty(GccRequiredToolMentions.HomeUrlByName(null, []));
        Assert.Empty(GccRequiredToolMentions.HomeUrlByName(null, null));
    }

    [Fact]
    public void The_lookup_is_case_insensitive_on_the_name()
    {
        var byName = GccRequiredToolMentions.HomeUrlByName(null, Partners);

        Assert.Equal(byName["Tipalti"], byName["tipalti"]);
    }

    [Fact]
    public void A_non_http_url_contributes_nothing()
    {
        Assert.Empty(GccRequiredToolMentions.HomeUrlByName(null, ["ftp://tipalti.com", "not a url"]));
    }

    [Fact]
    public void Every_name_the_anchor_lookup_knows_can_be_asked_for_its_url()
    {
        // The two must agree on spelling. If AnchorLookup settles on "Zone & Co" and this keyed on
        // "Zoneandco", a caller holding the name from one would miss in the other.
        var anchors = GccRequiredToolMentions.AnchorLookup(null, Partners);
        var byName = GccRequiredToolMentions.HomeUrlByName(null, Partners);

        foreach (var name in anchors.Values)
        {
            Assert.True(byName.ContainsKey(name), $"no url for '{name}'");
        }
    }
}
