using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.GeekSeo;

namespace GeekBackend.Tests;

public class GccMustMentionSubtopicsTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto TreeFor(string pageUrl, object roots) =>
        new(Guid.NewGuid(), Guid.NewGuid(), pageUrl, JsonSerializer.Serialize(roots, JsonOpts), DateTimeOffset.UtcNow);

    [Fact]
    public void Exact_slug_match_returns_child_headings_as_must_mention_block()
    {
        var tree = TreeFor("https://example.com", new object[]
        {
            new
            {
                level = 4,
                headingText = "AI Content Creation Workflow",
                paragraphs = new[] { "Some real body text." },
                children = new object[]
                {
                    new { level = 5, headingText = "Marketing", paragraphs = new[] { "Marketing text." }, children = Array.Empty<object>() },
                    new { level = 5, headingText = "Sales", paragraphs = new[] { "Sales text." }, children = Array.Empty<object>() },
                },
            },
        });

        var block = GccGenerateService.BuildMustMentionSubtopicsBlock([tree], "AI Content Creation Workflow");

        Assert.Contains("MUST MENTION", block);
        Assert.Contains("Marketing", block);
        Assert.Contains("Sales", block);
    }

    [Fact]
    public void Substring_slug_match_still_finds_the_right_node()
    {
        var tree = TreeFor("https://example.com", new object[]
        {
            new
            {
                level = 4,
                headingText = "AI Content Creation Workflow",
                paragraphs = new[] { "Real text." },
                children = new object[]
                {
                    new { level = 5, headingText = "Marketing", paragraphs = new[] { "Text." }, children = Array.Empty<object>() },
                },
            },
        });

        // Operator-typed topic is a superset of the real heading slug.
        var block = GccGenerateService.BuildMustMentionSubtopicsBlock([tree], "AI Content Creation Workflow Guide");

        Assert.Contains("Marketing", block);
    }

    [Fact]
    public void No_match_returns_empty_string_not_a_guess()
    {
        var tree = TreeFor("https://example.com", new object[]
        {
            new
            {
                level = 4,
                headingText = "Completely Unrelated Topic",
                paragraphs = new[] { "Text." },
                children = new object[]
                {
                    new { level = 5, headingText = "Something Else", paragraphs = new[] { "Text." }, children = Array.Empty<object>() },
                },
            },
        });

        var block = GccGenerateService.BuildMustMentionSubtopicsBlock([tree], "AI Content Creation Workflow");

        Assert.Equal(string.Empty, block);
    }

    [Fact]
    public void Matched_node_with_no_children_returns_empty_string()
    {
        var tree = TreeFor("https://example.com", new object[]
        {
            new { level = 4, headingText = "AI Content Creation Workflow", paragraphs = new[] { "Text." }, children = Array.Empty<object>() },
        });

        var block = GccGenerateService.BuildMustMentionSubtopicsBlock([tree], "AI Content Creation Workflow");

        Assert.Equal(string.Empty, block);
    }

    [Fact]
    public void Empty_topic_or_no_trees_returns_empty_string()
    {
        Assert.Equal(string.Empty, GccGenerateService.BuildMustMentionSubtopicsBlock([], "AI Content Creation Workflow"));
        Assert.Equal(
            string.Empty,
            GccGenerateService.BuildMustMentionSubtopicsBlock(
                [TreeFor("https://example.com", Array.Empty<object>())], ""));
    }
}
