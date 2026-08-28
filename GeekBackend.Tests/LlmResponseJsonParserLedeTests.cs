using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekBackend.Tests;

public sealed class LlmResponseJsonParserLedeTests
{
    [Fact]
    public void ParseLedeAndIntroduction_parses_full_response()
    {
        const string json = """
            {
              "lede": {
                "ledeType": "question",
                "heading": "Is Your Team Ready for AI?",
                "paragraphs": [{"type":"text","runs":[{"text":"Hook paragraph."}]}],
                "imagePrompt": "Enterprise team reviewing dashboards."
              },
              "introduction": {
                "tag": "h2",
                "heading": "Is Your Team Ready for AI?",
                "paragraphs": [{"type":"text","runs":[{"text":"Scope paragraph."}]}],
                "href": null,
                "children": [{
                  "tag": "h3",
                  "heading": "Who this is for",
                  "paragraphs": [{"type":"text","runs":[{"text":"Operators."}]}],
                  "href": null,
                  "children": []
                }]
              }
            }
            """;

        var (lede, ledeType, intro) = LlmResponseJsonParser.ParseLedeAndIntroduction(json, "pillar lede");

        Assert.Equal(LedeType.Question, ledeType);
        Assert.Equal("Is Your Team Ready for AI?", lede.Heading);
        Assert.Single(lede.Paragraphs);
        Assert.Equal("Is Your Team Ready for AI?", intro.Heading);
        Assert.Single(intro.Children);
    }

    [Fact]
    public void ParseLedeAndIntroduction_accepts_lede_only_without_introduction_key()
    {
        const string json = """
            {
              "lede": {
                "ledeType": "question",
                "heading": "Is Your Business Missing Opportunities?",
                "paragraphs": [{"type":"text","runs":[{"text":"Pain before solution."}]}],
                "imagePrompt": "Busy operations floor."
              }
            }
            """;

        var (lede, ledeType, intro) = LlmResponseJsonParser.ParseLedeAndIntroduction(json, "pillar lede");

        Assert.Equal(LedeType.Question, ledeType);
        Assert.Equal("Is Your Business Missing Opportunities?", lede.Heading);
        Assert.Equal(lede.Heading, intro.Heading);
        Assert.Empty(intro.Paragraphs);
        Assert.Empty(intro.Children);
    }

    [Fact]
    public void ParseLedeAndIntroduction_uses_lede_heading_when_intro_heading_blank()
    {
        const string json = """
            {
              "lede": {
                "ledeType": "summary",
                "heading": "Understanding AI Marketing",
                "paragraphs": [{"type":"text","runs":[{"text":"Hook."}]}],
                "imagePrompt": "Marketing analytics."
              },
              "introduction": {
                "tag": "h2",
                "heading": "",
                "paragraphs": [{"type":"text","runs":[{"text":"Scope."}]}],
                "href": null,
                "children": []
              }
            }
            """;

        var (_, _, intro) = LlmResponseJsonParser.ParseLedeAndIntroduction(json, "pillar lede");

        Assert.Equal("Understanding AI Marketing", intro.Heading);
        Assert.Single(intro.Paragraphs);
    }

    [Fact]
    public void ParseLedeAndIntroduction_preserves_children_nested_under_lede()
    {
        const string json = """
            {
              "lede": {
                "ledeType": "directAddress",
                "heading": "Your AI Roadmap Starts Here",
                "paragraphs": [{"type":"text","runs":[{"text":"You need a plan."}]}],
                "imagePrompt": "Roadmap illustration.",
                "children": [{
                  "tag": "h3",
                  "heading": "Who this is for",
                  "paragraphs": [{"type":"text","runs":[{"text":"Marketing leaders."}]}],
                  "href": null,
                  "children": []
                }]
              }
            }
            """;

        var (lede, _, intro) = LlmResponseJsonParser.ParseLedeAndIntroduction(json, "pillar lede");
        var merged = GccV2WriteOutlineRules.MergeLedeAndIntroduction(lede, intro);

        Assert.Empty(intro.Paragraphs);
        Assert.Single(intro.Children);
        Assert.Single(merged.Children);
        Assert.Equal("Who this is for", merged.Children[0].Heading);
    }
}
