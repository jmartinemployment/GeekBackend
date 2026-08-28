using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekSeo;

namespace GeekBackend.Tests;

public class PartnerToolLinkFilterTests
{
    [Theory]
    [InlineData("Privacy Policy", "/privacy", false)]
    [InlineData("Call Us (561) 526-3512", "tel:5615263512", false)]
    [InlineData("Headquarters Delray Beach, Fl", "/contact", false)]
    [InlineData("Get Your Free AI Assessment", "/assessment", false)]
    [InlineData("read our comprehensive guide: \"How Smart Chatbots Revolutionize B2B Marketing\"", "/blog/x", false)]
    [InlineData("BotPenguin", "/tools/marketing/bot-penguin", true)]
    [InlineData("ManyChat", "/tools/marketing/many-chat", true)]
    [InlineData("Pipedrive", "https://geekatyourspot.com/tools/marketing/pipedrive", true)]
    public void IsLikelyPartnerToolLink_filters_chrome_keeps_tools(string name, string href, bool expected)
    {
        Assert.Equal(expected, GccGenerateService.IsLikelyPartnerToolLink(name, href));
    }

    [Fact]
    public void ParseHierarchyTools_accepts_comma_separated_tool_row()
    {
        var tools = GccGenerateService.ParseHierarchyTools(
            ["BotPenguin, ManyChat, Pipedrive, CustomGPT, Get Chip Bot."],
            [
                new("BotPenguin", "/tools/marketing/bot-penguin"),
                new("ManyChat", "/tools/marketing/many-chat"),
                new("Pipedrive", "/tools/marketing/pipedrive"),
                new("CustomGPT", "/tools/marketing/custom-gpt"),
                new("Get Chip Bot", "/tools/marketing/getchipbot"),
            ]);

        Assert.Equal(5, tools.Count);
        Assert.Contains(tools, t => t.Name == "BotPenguin");
        Assert.Contains(tools, t => t.Name == "Get Chip Bot");
    }

    [Fact]
    public void ParseHierarchyTools_accepts_tool_row_after_use_case_blurb_on_same_heading()
    {
        // Live homepage markup: H5 has long prose + "Top AI Chatbot Tools:" <p> + tool <p> with anchors.
        var tools = GccGenerateService.ParseHierarchyTools(
            [
                "Marketing teams use smart AI chatbots to run campaigns around the clock, sort and qualify leads, deliver personal product tips, and recover lost sales.",
                "Top AI Chatbot Tools:",
            ],
            [
                new("BotPenguin", "/tools/marketing/bot-penguin"),
                new("ManyChat", "/tools/marketing/many-chat"),
                new("Pipedrive", "/tools/marketing/pipedrive"),
                new("CustomGPT", "/tools/marketing/custom-gpt"),
                new("Get Chip Bot", "/tools/marketing/getchipbot"),
            ]);

        Assert.Equal(5, tools.Count);
        Assert.Contains(tools, t => t.Name == "BotPenguin");
    }

    [Fact]
    public void ExtractToolsFromTrees_five_tools_without_operator_paste()
    {
        const string treeJson = """
            [
              {
                "level": 5,
                "headingText": "Smart Chatbots for Marketing:",
                "paragraphs": [
                  "Marketing teams use smart AI chatbots to run campaigns around the clock.",
                  "Top AI Chatbot Tools:"
                ],
                "links": [
                  { "text": "BotPenguin", "href": "/tools/marketing/bot-penguin" },
                  { "text": "ManyChat", "href": "/tools/marketing/many-chat" },
                  { "text": "Pipedrive", "href": "/tools/marketing/pipedrive" },
                  { "text": "CustomGPT", "href": "/tools/marketing/custom-gpt" },
                  { "text": "Get Chip Bot", "href": "/tools/marketing/getchipbot" }
                ],
                "children": []
              }
            ]
            """;

        var trees = new List<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "https://geekatyourspot.com/", treeJson, DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), Guid.NewGuid(), "https://geekatyourspot.com/about", """
                [{ "level": 1, "headingText": "About", "paragraphs": ["Company."], "links": [], "children": [] }]
                """, DateTimeOffset.UtcNow),
        };

        var tools = GccGenerateService.ExtractToolsFromTrees(
            trees, "Smart Chatbots for Marketing", null, null);

        Assert.Equal(5, tools.Count);
        Assert.Contains(tools, t => t.Name == "BotPenguin");
        Assert.Contains(tools, t => t.Name == "Get Chip Bot");
    }

    [Fact]
    public void BuildHierarchyMatchesFromTrees_ranks_five_tool_slice_first()
    {
        const string treeJson = """
            [
              {
                "level": 5,
                "headingText": "Smart Chatbots for Marketing:",
                "paragraphs": ["Older copy."],
                "links": [],
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
              },
              {
                "level": 5,
                "headingText": "Smart Chatbots for Marketing:",
                "paragraphs": [
                  "Marketing teams use smart AI chatbots.",
                  "Top AI Chatbot Tools:"
                ],
                "links": [
                  { "text": "BotPenguin", "href": "/tools/marketing/bot-penguin" },
                  { "text": "ManyChat", "href": "/tools/marketing/many-chat" },
                  { "text": "Pipedrive", "href": "/tools/marketing/pipedrive" },
                  { "text": "CustomGPT", "href": "/tools/marketing/custom-gpt" },
                  { "text": "Get Chip Bot", "href": "/tools/marketing/getchipbot" }
                ],
                "children": []
              }
            ]
            """;

        var trees = new List<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "https://geekatyourspot.com/", treeJson, DateTimeOffset.UtcNow),
        };

        var matches = GccGenerateService.BuildHierarchyMatchesFromTrees(
            trees, "Smart Chatbots for Marketing");
        Assert.NotEmpty(matches);
        var top = matches[0];
        var tools = GccGenerateService.ExtractToolsFromAssignmentMarkdown(
            top.AssignmentMarkdown, top.MatchedHeading);
        Assert.Equal(5, tools.Count);
        Assert.DoesNotContain(tools, t => t.Name == "Intercom");
    }

    [Fact]
    public void ExtractToolsFromTrees_prefers_five_tool_row_over_stale_three_tool_copy()
    {
        // Site still has an old Intercom/Tidio/HubSpot H6 copy and a newer five-tool row on the H5.
        const string treeJson = """
            [
              {
                "level": 5,
                "headingText": "Smart Chatbots for Marketing:",
                "paragraphs": [
                  "Marketing teams use smart AI chatbots to run campaigns around the clock.",
                  "Top AI Chatbot Tools:"
                ],
                "links": [
                  { "text": "BotPenguin", "href": "/tools/marketing/bot-penguin" },
                  { "text": "ManyChat", "href": "/tools/marketing/many-chat" },
                  { "text": "Pipedrive", "href": "/tools/marketing/pipedrive" },
                  { "text": "CustomGPT", "href": "/tools/marketing/custom-gpt" },
                  { "text": "Get Chip Bot", "href": "/tools/marketing/getchipbot" }
                ],
                "children": []
              },
              {
                "level": 5,
                "headingText": "Smart Chatbots for Marketing:",
                "paragraphs": ["Older copy."],
                "links": [],
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
            """;

        var trees = new List<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "https://geekatyourspot.com/", treeJson, DateTimeOffset.UtcNow),
        };

        var tools = GccGenerateService.ExtractToolsFromTrees(
            trees, "Smart Chatbots for Marketing", null, null);

        Assert.Equal(5, tools.Count);
        Assert.Contains(tools, t => t.Name == "BotPenguin");
        Assert.DoesNotContain(tools, t => t.Name == "Intercom");
    }

    [Fact]
    public void ExtractToolsFromTrees_returns_tool_list_not_site_chrome()
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
                          { "text": "BotPenguin", "href": "/tools/marketing/bot-penguin" },
                          { "text": "ManyChat", "href": "/tools/marketing/many-chat" },
                          { "text": "Pipedrive", "href": "/tools/marketing/pipedrive" },
                          { "text": "CustomGPT", "href": "/tools/marketing/custom-gpt" },
                          { "text": "Get Chip Bot", "href": "/tools/marketing/getchipbot" }
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

        Assert.Equal(5, tools.Count);
        Assert.Contains(tools, t => t.Name == "BotPenguin");
        Assert.Contains(tools, t => t.Name == "ManyChat");
        Assert.Contains(tools, t => t.Name == "Pipedrive");
        Assert.Contains(tools, t => t.Name == "CustomGPT");
        Assert.Contains(tools, t => t.Name == "Get Chip Bot");
        Assert.DoesNotContain(tools, t => t.Name.Contains("Privacy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tools, t => t.Name.Contains("Call Us", StringComparison.OrdinalIgnoreCase));
    }
}
