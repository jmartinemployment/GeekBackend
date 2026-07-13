using GeekApplication.Blog;
using GeekApplication.Models.Blog;

namespace GeekBackend.Tests;

public class SchemaMetadataParserTests
{
    [Fact]
    public void Parse_flat_TechnicalArticle_returns_description()
    {
        const string schema = """
            {
              "@context": "https://schema.org",
              "@type": "TechnicalArticle",
              "headline": "Expense Management",
              "description": "SEO meta for the pillar."
            }
            """;

        var result = SchemaMetadataParser.Parse("TechnicalArticle", schema);

        Assert.IsType<TechnicalArticleMetadata>(result);
        Assert.Equal("SEO meta for the pillar.", result!.Description);
    }

    [Fact]
    public void Parse_graph_TechnicalArticle_picks_article_node_not_SoftwareApplication()
    {
        const string schema = """
            {
              "@context": "https://schema.org",
              "@graph": [
                {
                  "@type": "TechnicalArticle",
                  "headline": "Expense Management",
                  "description": "Pillar SEO description."
                },
                {
                  "@type": "SoftwareApplication",
                  "name": "Ramp",
                  "description": "App blurb."
                }
              ]
            }
            """;

        var result = SchemaMetadataParser.Parse("TechnicalArticle", schema);

        Assert.IsType<TechnicalArticleMetadata>(result);
        Assert.Equal("Pillar SEO description.", result!.Description);
    }

    [Fact]
    public void Parse_flat_NewsArticle_returns_description()
    {
        const string schema = """
            {
              "@context": "https://schema.org",
              "@type": "NewsArticle",
              "headline": "Ramp",
              "description": "Tool page SEO meta."
            }
            """;

        var result = SchemaMetadataParser.Parse("NewsArticle", schema);

        Assert.IsType<NewsArticleMetadata>(result);
        Assert.Equal("Tool page SEO meta.", result!.Description);
    }

    [Fact]
    public void Parse_graph_NewsArticle_returns_tool_metadata()
    {
        const string schema = """
            {
              "@context": "https://schema.org",
              "@graph": [
                {
                  "@type": "NewsArticle",
                  "headline": "Ramp",
                  "description": "Sponsored tool SEO."
                },
                {
                  "@type": "SoftwareApplication",
                  "name": "Ramp"
                }
              ]
            }
            """;

        var result = SchemaMetadataParser.Parse("NewsArticle", schema);

        Assert.IsType<NewsArticleMetadata>(result);
        Assert.Equal("Sponsored tool SEO.", result!.Description);
    }

    [Fact]
    public void Parse_empty_or_invalid_returns_null()
    {
        Assert.Null(SchemaMetadataParser.Parse("BlogPosting", "{}"));
        Assert.Null(SchemaMetadataParser.Parse("BlogPosting", ""));
        Assert.Null(SchemaMetadataParser.Parse("BlogPosting", "not-json"));
    }

    [Fact]
    public void Parse_graph_with_matching_root_node_when_children_miss()
    {
        const string schema = """
            {
              "@context": "https://schema.org",
              "@type": "TechnicalArticle",
              "headline": "Root article",
              "description": "Root SEO.",
              "@graph": [
                { "@type": "SoftwareApplication", "name": "Ramp" }
              ]
            }
            """;

        var result = SchemaMetadataParser.Parse("TechnicalArticle", schema);

        Assert.IsType<TechnicalArticleMetadata>(result);
        Assert.Equal("Root SEO.", result!.Description);
    }
}
