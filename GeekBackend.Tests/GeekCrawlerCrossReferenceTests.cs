using GeekAPI.Services.GeekCrawler;
using Xunit;

namespace GeekBackend.Tests;

/// <summary>
/// Which outside hosts a crawled site reaches, and from which sections.
/// </summary>
public class GeekCrawlerCrossReferenceTests
{
    private static SiteStructureLink Link(string label, string href) =>
        new(label, href, "", "paragraph");

    private static SiteStructureNode Node(int level, string heading, params SiteStructureLink[] links) =>
        new(level, heading, [], links, []);

    [Fact]
    public void An_internal_tool_page_carries_the_partner_it_names()
    {
        // The hop this exists for. The section links an on-site tool page, so on its own it looks
        // self-contained; the partner is one step further, in that page's own anchors.
        var services = new SiteStructurePage("https://geekatyourspot.com/services", [
            Node(2, "Automated Data Entry & Processing",
                Link("Melio", "https://geekatyourspot.com/tools/accounting/melio")),
        ]);

        var toolPage = new SiteStructurePage("https://geekatyourspot.com/tools/accounting/melio", [
            Node(1, "Melio", Link("Visit Melio", "https://melio.com/pricing")),
        ]);

        var xref = GeekCrawlerSiteStructure.BuildCrossReference([services, toolPage]);

        var melio = Assert.Single(xref.Hosts, h => h.Host == "melio.com");

        // Two references, both true: the services section reaches melio.com through the tool page,
        // and the tool page - itself crawled - links it directly. Collapsing them would lose the
        // fact that a section other than the tool page's own reaches this partner.
        Assert.Equal(2, melio.References.Count);

        var viaHop = Assert.Single(melio.References, r => r.ViaPageUrl is not null);
        Assert.Equal("https://geekatyourspot.com/tools/accounting/melio", viaHop.ViaPageUrl);
        Assert.Equal("https://geekatyourspot.com/services", viaHop.PageUrl);
        Assert.Contains("Automated Data Entry & Processing", viaHop.SectionPath);

        var direct = Assert.Single(melio.References, r => r.ViaPageUrl is null);
        Assert.Equal("https://geekatyourspot.com/tools/accounting/melio", direct.PageUrl);
    }

    [Fact]
    public void A_direct_outbound_link_has_no_hop()
    {
        var page = new SiteStructurePage("https://geekatyourspot.com/about", [
            Node(2, "Our stack", Link("Melio", "https://melio.com")),
        ]);

        var reference = Assert.Single(
            Assert.Single(GeekCrawlerSiteStructure.BuildCrossReference([page]).Hosts).References);

        Assert.Null(reference.ViaPageUrl);
    }

    [Fact]
    public void The_site_is_not_its_own_outside_host()
    {
        // An internal link that resolves to no page in the run is not an outside party; it is a
        // page the crawl did not keep. Reporting it as external would invent a third-party
        // relationship out of a crawl gap.
        var page = new SiteStructurePage("https://geekatyourspot.com/services", [
            Node(2, "Services", Link("Missing", "https://geekatyourspot.com/not-crawled")),
        ]);

        Assert.Empty(GeekCrawlerSiteStructure.BuildCrossReference([page]).Hosts);
    }

    [Fact]
    public void Www_and_a_trailing_slash_resolve_to_the_same_page()
    {
        var services = new SiteStructurePage("https://geekatyourspot.com/services", [
            Node(2, "Services", Link("Melio", "https://www.geekatyourspot.com/tools/melio/")),
        ]);
        var tool = new SiteStructurePage("https://geekatyourspot.com/tools/melio", [
            Node(1, "Melio", Link("Site", "https://melio.com")),
        ]);

        var melio = Assert.Single(GeekCrawlerSiteStructure.BuildCrossReference([services, tool]).Hosts);
        Assert.Equal("melio.com", melio.Host);
    }

    [Fact]
    public void Relative_hrefs_resolve_against_the_page_they_are_on()
    {
        var page = new SiteStructurePage("https://geekatyourspot.com/services/data", [
            Node(2, "Tools", Link("Melio", "../tools/melio")),
        ]);
        var tool = new SiteStructurePage("https://geekatyourspot.com/tools/melio", [
            Node(1, "Melio", Link("Site", "https://melio.com")),
        ]);

        Assert.Single(GeekCrawlerSiteStructure.BuildCrossReference([page, tool]).Hosts, h => h.Host == "melio.com");
    }

    [Fact]
    public void An_unparseable_href_is_counted_not_dropped()
    {
        // A site full of malformed links must read as that, not as a site with few links.
        // An anchor with no href at all is the honest case: the crawler records { label, href },
        // and a label with nothing behind it resolves to nowhere.
        var page = new SiteStructurePage("https://geekatyourspot.com/services", [
            Node(2, "Services", Link("Broken", "")),
        ]);

        var xref = GeekCrawlerSiteStructure.BuildCrossReference([page]);

        Assert.Empty(xref.Hosts);
        Assert.Equal(1, xref.UnresolvedAnchors);
    }

    [Fact]
    public void Hosts_are_ordered_by_how_often_they_are_referenced()
    {
        var page = new SiteStructurePage("https://geekatyourspot.com/about", [
            Node(2, "One", Link("A", "https://rare.com")),
            Node(2, "Two", Link("B", "https://common.com")),
            Node(2, "Three", Link("C", "https://common.com/pricing")),
        ]);

        var hosts = GeekCrawlerSiteStructure.BuildCrossReference([page]).Hosts;

        Assert.Equal("common.com", hosts[0].Host);
        Assert.Equal(2, hosts[0].References.Count);
    }
}
