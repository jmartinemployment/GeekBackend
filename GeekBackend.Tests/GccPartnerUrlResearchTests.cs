using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests;

public class GccPartnerUrlResearchTests
{
    [Fact]
    public void CollectPartnerToolRows_bare_urls_alone_invent_zero_tools()
    {
        const string brief = """
            {
              "operatorTools": [
                "https://botpenguin.com/",
                "https://manychat.com/",
                "https://www.pipedrive.com/en/products/sales/leads",
                "https://customgpt.ai/",
                "https://getchipbot.com/"
              ]
            }
            """;

        var rows = GccPartnerUrlResearchService.CollectPartnerToolRows(brief);

        Assert.Empty(rows);
    }

    [Fact]
    public void CollectPartnerToolRows_crawl_five_without_paste()
    {
        const string brief = """
            {
              "hierarchyPlan": {
                "recommendedTools": [
                  { "name": "BotPenguin", "href": "/tools/marketing/bot-penguin" },
                  { "name": "ManyChat", "href": "/tools/marketing/many-chat" },
                  { "name": "Pipedrive", "href": "/tools/marketing/pipedrive" },
                  { "name": "CustomGPT", "href": "/tools/marketing/custom-gpt" },
                  { "name": "Get Chip Bot", "href": "/tools/marketing/getchipbot" }
                ]
              }
            }
            """;

        var rows = GccPartnerUrlResearchService.CollectPartnerToolRows(brief);

        Assert.Equal(5, rows.Count);
        Assert.Contains(rows, r => r.Name == "BotPenguin");
        Assert.Contains(rows, r => r.Name == "Get Chip Bot");
        Assert.All(rows, r => Assert.Equal("crawl", r.Source));
    }

    [Fact]
    public void CollectPartnerToolRows_attaches_operator_urls_onto_crawl_names()
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

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, hrefs.Count);
        Assert.Contains(hrefs, h => h.Contains("botpenguin.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hrefs, h => h.Contains("manychat.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(hrefs, h => h.Contains("geekatyourspot.com", StringComparison.OrdinalIgnoreCase));
        Assert.All(rows, r => Assert.Equal("operator", r.Source));
    }

    [Fact]
    public void CollectPartnerToolRows_attaches_bare_operator_url_by_host_to_crawl_name()
    {
        const string brief = """
            {
              "hierarchyPlan": {
                "recommendedTools": [
                  { "name": "BotPenguin", "href": "/tools/marketing/bot-penguin" },
                  { "name": "Pipedrive", "href": "/tools/marketing/pipedrive" }
                ]
              },
              "operatorTools": [
                "https://botpenguin.com/",
                "https://www.pipedrive.com/en/products/sales/leads"
              ]
            }
            """;

        var rows = GccPartnerUrlResearchService.CollectPartnerToolRows(brief);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r =>
            r.Name == "BotPenguin"
            && r.Url != null
            && r.Url.Contains("botpenguin.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rows, r =>
            r.Name == "Pipedrive"
            && r.Url != null
            && r.Url.Contains("pipedrive.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rows, r => r.Name.Equals("leads", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CollectPartnerHrefs_uses_crawl_absolute_when_no_operator_url()
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
        var rows = GccPartnerUrlResearchService.CollectPartnerToolRows(brief);

        // Tidio/HubSpot are operator-only — not crawl tools — so they do not appear.
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Name == "Intercom");
        Assert.Contains(rows, r => r.Name == "Dup");
        Assert.Single(hrefs);
        Assert.Contains(hrefs, h => h.Contains("intercom.com", StringComparison.OrdinalIgnoreCase));
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
