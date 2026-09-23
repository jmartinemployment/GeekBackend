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
        // A lede has no headline of its own -- it runs directly under the page title. It carried
        // one until 2026-09-23, rendered as an h2, so every page showed two headlines stacked.
        Assert.Equal(string.Empty, lede.Heading);
        Assert.Single(lede.Paragraphs);
        Assert.Equal(string.Empty, intro.Heading);
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
        Assert.Equal(string.Empty, lede.Heading);
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

        Assert.Equal(string.Empty, intro.Heading);
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

    [Fact]
    public void ParseLede_accepts_a_heading_less_lede()
    {
        // The shape the contract now asks for, and the one both acceptance checks rejected. Nothing
        // covered this path, so the suite stayed green while every blog and tool lede was refused:
        // "Model did not return a valid lede for blog lede" on a response that complied exactly.
        const string json = """
            {
              "ledeType": "anecdotal",
              "paragraphs": [{"type":"text","runs":[{"text":"Jessica Martin sat buried under paperwork."}]}]
            }
            """;

        var (lede, ledeType) = LlmResponseJsonParser.ParseLede(json, "blog lede");

        Assert.Equal(LedeType.Anecdotal, ledeType);
        Assert.Equal(string.Empty, lede.Heading);
        Assert.Single(lede.Paragraphs);
    }

    [Fact]
    public void ParseLede_still_refuses_a_lede_with_no_paragraphs()
    {
        // Paragraphs are what make it a lede, so an empty one is not a lede that happens to be
        // short -- it is nothing, and must fail rather than render as a blank opening.
        const string json = """{"ledeType": "anecdotal", "paragraphs": []}""";

        Assert.Throws<GeekAPI.Services.Workflow.Providers.ContentGenerationException>(
            () => LlmResponseJsonParser.ParseLede(json, "blog lede"));
    }
}
