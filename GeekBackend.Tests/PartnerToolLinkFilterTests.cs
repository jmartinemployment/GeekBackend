using GeekAPI.Services.ContentCreator;

namespace GeekBackend.Tests;

public class PartnerToolLinkFilterTests
{
    [Theory]
    [InlineData("Privacy Policy", "/privacy", false)]
    [InlineData("Call Us (561) 526-3512", "tel:5615263512", false)]
    [InlineData("Headquarters Delray Beach, Fl", "/contact", false)]
    [InlineData("Get Your Free AI Assessment", "/assessment", false)]
    [InlineData("read our comprehensive guide: \"How Smart Chatbots Revolutionize B2B Marketing\"", "/blog/x", false)]
    [InlineData("Intercom", "/tools/marketing/intercom", true)]
    [InlineData("Tidio", "/tools/marketing/tidio", true)]
    [InlineData("HubSpot", "https://geekatyourspot.com/tools/marketing/hubspot", true)]
    public void IsLikelyPartnerToolLink_filters_chrome_keeps_tools(string name, string href, bool expected)
    {
        Assert.Equal(expected, GccGenerateService.IsLikelyPartnerToolLink(name, href));
    }

    [Fact]
    public void ExtractToolsFromTrees_prefers_tools_path_over_site_chrome()
    {
        const string treeJson = """
            [
              {
                "level": 4,
                "headingText": "Lead Capture Pipeline",
                "paragraphs": [],
                "links": [
                  { "text": "Privacy Policy", "href": "/privacy" },
                  { "text": "Call Us (561) 526-3512", "href": "tel:5615263512" },
                  { "text": "Get Your Free AI Assessment", "href": "/assessment" }
                ],
                "children": [
                  {
                    "level": 5,
                    "headingText": "Smart Chatbots for Marketing:",
                    "paragraphs": ["Marketing teams use smart AI chatbots."],
                    "links": [
                      { "text": "read our comprehensive guide: \"How Smart Chatbots Revolutionize B2B Marketing\"", "href": "/blog/guide" }
                    ],
                    "children": [
                      {
                        "level": 6,
                        "headingText": "Top AI Chatbot Tools:",
                        "paragraphs": [],
                        "links": [
                          { "text": "Intercom", "href": "/tools/marketing/intercom" },
                          { "text": "Tidio", "href": "/tools/marketing/tidio" },
                          { "text": "HubSpot", "href": "/tools/marketing/hubspot" }
                        ],
                        "children": []
                      }
                    ]
                  }
                ]
              }
            ]
            """;

        var trees = new List<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto>
        {
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://geekatyourspot.com/",
                treeJson,
                DateTimeOffset.UtcNow),
        };

        var tools = GccGenerateService.ExtractToolsFromTrees(
            trees, "Smart Chatbots for Marketing", null, null);

        Assert.Equal(3, tools.Count);
        Assert.Contains(tools, t => t.Name == "Intercom");
        Assert.Contains(tools, t => t.Name == "Tidio");
        Assert.Contains(tools, t => t.Name == "HubSpot");
        Assert.DoesNotContain(tools, t => t.Name.Contains("Privacy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tools, t => t.Name.Contains("Call Us", StringComparison.OrdinalIgnoreCase));
    }
}
