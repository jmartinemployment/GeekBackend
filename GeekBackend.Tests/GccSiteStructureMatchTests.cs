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
        // The crawler records an anchor as { label, href } and nothing more. A label like
        // "Learn more" is useless to a writer on its own; the block it sits in is what says
        // what the link is about, so it travels with the link.
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

        var vague = Assert.Single(match.RecommendedTools, t => t.Name == "Learn more");
        Assert.Contains("supplier PDF", vague.Context);
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
