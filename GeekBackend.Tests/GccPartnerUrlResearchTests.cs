using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests;

public class GccPartnerUrlResearchTests
{
    [Fact]
    public void CollectPartnerHrefs_prefers_operator_destination_over_crawl()
    {
        const string brief = """
            {
              "hierarchyPlan": {
                "recommendedTools": [
                  { "name": "BotPenguin", "href": "https://geekatyourspot.com/tools/marketing/bot-penguin" },
                  { "name": "ManyChat", "href": "https://geekatyourspot.com/tools/marketing/many-chat" }
                ]
              },
              "operatorTools": [
                { "name": "BotPenguin", "url": "https://botpenguin.com/" },
                { "name": "ManyChat", "url": "https://manychat.com/" }
              ]
            }
            """;

        var hrefs = GccPartnerUrlResearchService.CollectPartnerHrefs(brief);
        var rows = GccPartnerUrlResearchService.CollectPartnerToolRows(brief);

        Assert.Equal(2, hrefs.Count);
        Assert.Contains(hrefs, h => h.Contains("botpenguin.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hrefs, h => h.Contains("manychat.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(hrefs, h => h.Contains("geekatyourspot.com", StringComparison.OrdinalIgnoreCase));
        Assert.All(rows.Where(r => r.Name is "BotPenguin" or "ManyChat"), r =>
            Assert.Equal("operator", r.Source));
    }

    [Fact]
    public void CollectPartnerHrefs_reads_hierarchy_and_operator_urls()
    {
        const string brief = """
            {
              "hierarchyPlan": {
                "recommendedTools": [
                  { "name": "Intercom", "href": "https://www.intercom.com/a" },
                  { "name": "Dup", "href": "https://www.intercom.com/a" }
                ]
              },
              "operatorTools": [
                { "name": "Tidio", "url": "https://www.tidio.com/b" },
                "https://www.hubspot.com/c"
              ]
            }
            """;

        var hrefs = GccPartnerUrlResearchService.CollectPartnerHrefs(brief);

        Assert.Equal(3, hrefs.Count);
        Assert.Contains(hrefs, h => h.Contains("intercom.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hrefs, h => h.Contains("tidio.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hrefs, h => h.Contains("hubspot.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MergePartnerResearchIntoBriefJson_writes_partnerResearch_array()
    {
        var pages = new List<GccQuoteablePage>
        {
            new(
                "https://example.com/tool",
                "Example Tool",
                [new HeadingDto(2, "Features")],
                ["Example Tool helps teams reply faster with shared inboxes and automation."]),
        };

        var merged = GccPartnerUrlResearchService.MergePartnerResearchIntoBriefJson("{\"title\":\"t\"}", pages);
        Assert.NotNull(merged);
        Assert.Contains("partnerResearch", merged!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Example Tool", merged!, StringComparison.Ordinal);
        Assert.Contains("shared inboxes", merged!, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractPartnerPage_keeps_far_more_than_upload_caps()
    {
        var paragraphs = string.Concat(
            Enumerable.Range(1, 40).Select(i =>
                $"<p>Partner paragraph number {i} with enough characters to pass the length filter for extraction.</p>"));
        var html = $"<html><head><title>Big Partner Page</title></head><body><h1>Big Partner Page</h1>{paragraphs}</body></html>";

        var upload = GccArticleHtmlExtractor.Extract("https://example.com/u", html);
        var partner = GccArticleHtmlExtractor.ExtractPartnerPage("https://example.com/p", html);

        Assert.True(partner.Paragraphs.Count > upload.Paragraphs.Count);
        Assert.True(partner.Paragraphs.Count > GccResearchCaps.MaxParagraphsPerPage);
        Assert.False(GccArticleHtmlExtractor.IsEmpty(partner));
    }
}
