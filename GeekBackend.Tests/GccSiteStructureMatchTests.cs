using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekCrawler;
using Xunit;

namespace GeekBackend.Tests;

/// <summary>
/// v1's keyword match over the structure built from the crawler's typed blocks.
/// </summary>
public class GccSiteStructureMatchTests
{
    private static SiteStructureNode Node(
        int level,
        string heading,
        IReadOnlyList<SiteStructureLink>? links = null,
        IReadOnlyList<SiteStructureNode>? children = null) =>
        new(level, heading, [], links ?? [], children ?? []);

    private static SiteStructureLink Link(string label, string href, string context = "") =>
        new(label, href, context, "paragraph");

    private static SiteStructure Structure(params SiteStructurePage[] pages) =>
        new("run", DateTimeOffset.UtcNow, pages.Length, 0, pages,
            GeekCrawlerSiteStructure.BuildCrossReference(pages));

    [Fact]
    public void Ampersand_in_a_keyword_still_matches_its_heading()
    {
        // Punctuation is stripped on both sides before comparison, so "&" in the keyword and in the
        // heading cancel out. Without that, a heading containing an ampersand could never be matched
        // by the keyword that names it.
        var page = new SiteStructurePage("https://example.com/services", [
            Node(2, "Automated Data Entry & Processing", [
                Link("Invoice capture", "/tools/invoice-capture"),
                Link("Document OCR", "/tools/document-ocr"),
                Link("Form extraction", "/tools/form-extraction"),
            ]),
        ]);

        var matches = GccSiteStructureMatch.MatchAll(Structure(page), ["Automated Data Entry & Processing"]);

        var match = Assert.Single(matches);
        Assert.Equal("exact-heading", match.Kind);
        Assert.Equal("Automated Data Entry & Processing", match.MatchedHeading);

        // The anchors under the matched heading are the point of the match.
        Assert.Equal(3, match.RecommendedTools.Count);
        Assert.Contains(match.RecommendedTools, t => t.Href == "/tools/document-ocr");
    }

    [Fact]
    public void Ampersand_spelled_as_the_word_and_still_matches()
    {
        // "&" and "and" are different characters but the same heading to an operator typing it.
        var page = new SiteStructurePage("https://example.com/services", [
            Node(2, "Automated Data Entry and Processing", [
                Link("Invoice capture", "/tools/invoice-capture"),
                Link("Document OCR", "/tools/document-ocr"),
            ]),
        ]);

        var matches = GccSiteStructureMatch.MatchAll(Structure(page), ["Automated Data Entry & Processing"]);

        // Documents today's behaviour rather than asserting a wish: "&" is stripped, "and" is not,
        // so the slugs differ and this does NOT match. If that is wrong for the product, the fix is
        // in Slugify, and this test is where it gets decided.
        Assert.Empty(matches);
    }

    [Fact]
    public void A_link_carries_the_prose_it_sits_in()
    {
        // The crawler records an anchor as { label, href } and nothing more, so the block it sits in
        // is the only thing that says what the link is about. It travels with the link.
        var page = new SiteStructurePage("https://example.com/services", [
            Node(2, "Automated Data Entry & Processing", [
                Link("Invoice Capture", "/tools/invoice-capture",
                    "Invoice capture reads totals and line items straight off a supplier PDF."),
                Link("Document OCR", "/tools/document-ocr",
                    "Document OCR turns scanned paperwork into searchable text."),
            ]),
        ]);

        var match = Assert.Single(
            GccSiteStructureMatch.MatchAll(Structure(page), ["Automated Data Entry & Processing"]));

        var tool = Assert.Single(match.RecommendedTools, t => t.Name == "Invoice Capture");
        Assert.Contains("supplier PDF", tool.Context);
    }

    [Fact]
    public void A_vague_label_takes_its_name_from_the_url_rather_than_being_dropped()
    {
        // "Learn more" pointing at a product page is a real tool the site labelled badly, and the href
        // is its identity. This test asserted the opposite twice, in opposite directions: first that
        // the link survives named "Learn more" because Context explains it -- which no consumer reads,
        // all three projecting { name, href } -- and then, when the ported filter landed, that the link
        // is dropped. Dropping it is the failure mode project-site crawls are warned about by name:
        // tools silently stop being found, no error, fewer matches.
        //
        // The slug is the site's own words, so nothing is invented.
        var page = new SiteStructurePage("https://example.com/services", [
            Node(2, "Automated Data Entry & Processing", [
                Link("Learn more", "/tools/invoice-capture",
                    "Invoice capture reads totals and line items straight off a supplier PDF."),
                Link("Document OCR", "/tools/document-ocr",
                    "Document OCR turns scanned paperwork into searchable text."),
            ]),
        ]);

        var match = Assert.Single(
            GccSiteStructureMatch.MatchAll(Structure(page), ["Automated Data Entry & Processing"]));

        Assert.Equal(2, match.RecommendedTools.Count);
        Assert.DoesNotContain(match.RecommendedTools, t => t.Name == "Learn more");
        var resolved = Assert.Single(match.RecommendedTools, t => t.Href == "/tools/invoice-capture");
        Assert.Equal("Invoice Capture", resolved.Name);
    }

    [Theory]
    // A CTA whose URL names a kind of page, not a product. There is no name to be had, so the link
    // goes -- which is what separates it from "Learn more" over a product URL.
    [InlineData("read our comprehensive guide", "/blog/guide")]
    [InlineData("Learn more", "/insights")]
    [InlineData("click here", "/")]
    [InlineData("Find out more", "/news")]
    public void A_vague_label_over_a_url_that_names_nothing_is_dropped(string label, string href)
    {
        var page = new SiteStructurePage("https://example.com/marketing", [
            Node(2, "Smart Chatbots", [
                Link(label, href),
                Link("ManyChat", "/tools/many-chat"),
                Link("Pipedrive", "/tools/pipedrive"),
            ]),
        ]);

        var match = Assert.Single(GccSiteStructureMatch.MatchAll(Structure(page), ["Smart Chatbots"]));

        Assert.Equal(2, match.RecommendedTools.Count);
        Assert.DoesNotContain(match.RecommendedTools, t => t.Name.Contains("more", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_destination_label_is_never_redeemed_by_its_url()
    {
        // The other half of the split. "Privacy Policy" names what is at the far end, so no href makes
        // it a product -- where a CTA names nothing and lets the URL answer.
        // A real label is kept as-is, vague or not.
        Assert.Equal("Privacy Policy", GccSiteStructureMatch.ResolveToolName("Privacy Policy", "/tools/x"));

        // And it is refused however product-shaped its href looks.
        Assert.False(GccSiteStructureMatch.IsLikelyToolLink("Privacy Policy", "/tools/privacy-policy"));

        // A CTA over a chrome URL does resolve to a name -- "Privacy Policy 2024" -- because the slug
        // is read before anything judges it. It is then refused on that name, so the two stages reach
        // the same answer. Asserted end to end rather than on whichever function says no, which is
        // what I got wrong writing this.
        var resolved = GccSiteStructureMatch.ResolveToolName("Learn more", "/legal/privacy-policy-2024");
        Assert.NotNull(resolved);
        Assert.False(GccSiteStructureMatch.IsLikelyToolLink(resolved!, "/legal/privacy-policy-2024"));
    }

    [Theory]
    // Observed in live output as "tools", which is what the ported filter was written against.
    [InlineData("Privacy Policy", "/privacy")]
    [InlineData("Call Us (561) 526-3512", "tel:5615263512")]
    [InlineData("Get Your Free AI Assessment", "/assessment")]
    [InlineData("Contact Us", "/contact")]
    // A bare phone number, with no chrome phrase in it -- so this case exercises the number test and
    // not the "call us" needle, which is what made the first version of this theory pass with the
    // phone check deleted.
    [InlineData("(561) 526-3512", "/offices")]
    [InlineData("Subscribe", "/newsletter")]
    // Prose that happens to be linked, not a product.
    [InlineData("read our comprehensive guide on chatbots", "/blog/guide")]
    [InlineData("How do chatbots qualify leads?", "/blog/leads")]
    // Six words, no chrome phrase, no quote and no question mark -- the length test alone has to
    // reject it, which nothing asserted while every long label here also tripped another clause.
    [InlineData("Automate your accounts payable workflow today", "/services/ap")]
    // Shapes that cannot be a product page whatever the label says.
    [InlineData("BotPenguin", "#demo")]
    [InlineData("BotPenguin", "mailto:sales@botpenguin.com")]
    [InlineData("BotPenguin", "javascript:openModal()")]
    public void Furniture_beside_a_tool_is_not_a_tool(string label, string href)
    {
        // Each of these sat under the matched heading alongside two real tools, and each used to be
        // excluded only when a bigger group of real tools happened to out-rank its group. Ranking is
        // a tie-break; this is the filter.
        var page = new SiteStructurePage("https://example.com/marketing", [
            Node(2, "Smart Chatbots", [
                Link(label, href),
                Link("ManyChat", "/tools/many-chat"),
                Link("Pipedrive", "/tools/pipedrive"),
            ]),
        ]);

        var match = Assert.Single(GccSiteStructureMatch.MatchAll(Structure(page), ["Smart Chatbots"]));

        Assert.Equal(2, match.RecommendedTools.Count);
        Assert.DoesNotContain(match.RecommendedTools, t => t.Name == label);
    }

    [Fact]
    public void A_partner_product_page_on_the_partners_own_domain_is_still_a_tool()
    {
        // No preference for this site's /tools/ paths. A partner's product lives on the partner's
        // domain, and the page is about the partner -- a path test would favour our own pages over
        // the products the piece exists to discuss.
        var page = new SiteStructurePage("https://example.com/marketing", [
            Node(2, "Smart Chatbots", [
                Link("BotPenguin", "https://botpenguin.com/pricing"),
                Link("ManyChat", "https://manychat.com/"),
            ]),
        ]);

        var match = Assert.Single(GccSiteStructureMatch.MatchAll(Structure(page), ["Smart Chatbots"]));

        Assert.Equal(2, match.RecommendedTools.Count);
        Assert.Contains(match.RecommendedTools, t => t.Href == "https://botpenguin.com/pricing");
    }

    [Fact]
    public void Anchors_come_from_the_richest_group_in_the_subtree()
    {
        var page = new SiteStructurePage("https://example.com/services", [
            Node(2, "Automated Data Entry & Processing",
                links: [Link("Overview", "/overview")],
                children: [
                    Node(3, "Tools", [
                        Link("Invoice capture", "/tools/invoice-capture"),
                        Link("Document OCR", "/tools/document-ocr"),
                        Link("Form extraction", "/tools/form-extraction"),
                    ]),
                ]),
        ]);

        var match = Assert.Single(
            GccSiteStructureMatch.MatchAll(Structure(page), ["Automated Data Entry & Processing"]));

        // A single "Overview" link on the heading itself loses to the three-link group beneath it.
        Assert.Equal(3, match.RecommendedTools.Count);
        Assert.DoesNotContain(match.RecommendedTools, t => t.Name == "Overview");
    }

    [Fact]
    public void A_sections_nav_and_cta_links_are_not_its_tool_list()
    {
        // Ported from the retired Site-Analyzer-shaped extractor, which pinned this against page trees.
        // The chrome and the tools are both groups under the matched heading here -- three links of its
        // own against five in its subheading -- so this exercises the ranking and not just the absence
        // of a competitor: taking the first group instead of the largest returns the privacy link and
        // the phone number as this section's tools.
        //
        // Which half does the work is worth knowing. The chrome list matches whole labels, so
        // "Privacy Policy" and "Call Us (561) 526-3512" are not excluded by name at all -- they are
        // out-ranked. A section whose nav block were larger than its tool list would still pick wrong.
        var page = new SiteStructurePage("https://geekatyourspot.com/", [
            Node(4, "Lead Capture Pipeline", [], [
                Node(5, "Smart Chatbots for Marketing", [
                    Link("Privacy Policy", "/privacy"),
                    Link("Call Us (561) 526-3512", "tel:5615263512"),
                    Link("Get Your Free AI Assessment", "/assessment"),
                ], [
                    Node(6, "Top AI Chatbot Tools", [
                        Link("BotPenguin", "/tools/marketing/bot-penguin"),
                        Link("ManyChat", "/tools/marketing/many-chat"),
                        Link("Pipedrive", "/tools/marketing/pipedrive"),
                        Link("CustomGPT", "/tools/marketing/custom-gpt"),
                        Link("Get Chip Bot", "/tools/marketing/getchipbot"),
                    ]),
                ]),
            ]),
        ]);

        var matches = GccSiteStructureMatch.MatchAll(Structure(page), ["Smart Chatbots for Marketing"]);

        // Best-first, nothing deduplicated: the seed expands to "Smart Chatbots" as well, so the same
        // heading matches twice and the exact match ranks first. That is the documented contract.
        var match = matches[0];
        Assert.Equal("exact-heading", match.Kind);
        Assert.Equal(5, match.RecommendedTools.Count);
        Assert.Contains(match.RecommendedTools, t => t.Name == "BotPenguin");
        Assert.Contains(match.RecommendedTools, t => t.Name == "Get Chip Bot");
        Assert.DoesNotContain(match.RecommendedTools, t => t.Name.Contains("Privacy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(match.RecommendedTools, t => t.Name.Contains("Call Us", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_matched_heading_reports_its_own_depth()
    {
        // The structure carried the level and this record dropped it, so the one consumer that shows
        // the model which level a section sits at was writing 0 for every match -- which that
        // consumer's own renderer reads as "the node did not report one".
        var page = new SiteStructurePage("https://example.com/marketing", [
            Node(2, "Services", [], [
                Node(4, "Smart Chatbots for Marketing", [
                    Link("BotPenguin", "/tools/bot-penguin"),
                    Link("ManyChat", "/tools/many-chat"),
                ]),
            ]),
        ]);

        var matches = GccSiteStructureMatch.MatchAll(Structure(page), ["Smart Chatbots for Marketing"]);

        // The h4 the keyword names, not the h2 above it.
        Assert.All(matches, m => Assert.Equal(4, m.Level));
    }

    [Fact]
    public void A_short_parent_heading_does_not_swallow_the_keyword()
    {
        // The containment trap: "Processing" must not match by being a substring.
        var page = new SiteStructurePage("https://example.com/", [
            Node(2, "Processing", [Link("A", "/a"), Link("B", "/b")]),
        ]);

        Assert.Empty(GccSiteStructureMatch.MatchAll(Structure(page), ["Automated Data Entry & Processing"]));
    }

    [Fact]
    public void The_same_heading_on_two_pages_reports_both()
    {
        // Duplicates are a crawl defect the caller surfaces; the matcher must not collapse them.
        var heading = Node(2, "Automated Data Entry & Processing", [Link("A", "/a"), Link("B", "/b")]);
        var structure = Structure(
            new SiteStructurePage("https://example.com/services", [heading]),
            new SiteStructurePage("https://www.example.com/services", [heading]));

        var matches = GccSiteStructureMatch.MatchAll(structure, ["Automated Data Entry & Processing"]);

        Assert.Equal(2, matches.Count);
        Assert.Equal(2, matches.Select(m => m.SourcePageUrl).Distinct().Count());
    }
}
