using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.ContentCreatorV2.Publish;
using GeekAPI.Services.Rag;
using GeekAPI.Services.ContentCreatorV2.Write;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Goal: verified evidence must ship as schema.org citation attribution on the published
/// page, not be discarded after the internal pass/fail check. Appendix B gates display on
/// sourceRights ∈ {consented, licensed}.
/// </summary>
public sealed class GccV2JsonLdCitationTests
{
    private const string ArticleJsonLd = """
        {
          "@context": "https://schema.org",
          "@type": "TechArticle",
          "headline": "Tool review",
          "citation": [ { "@type": "BlogPosting", "url": "https://example.com/companion" } ]
        }
        """;

    private static RagCitationDto Citation(
        string url,
        bool? verified = true,
        string? rights = "consented",
        string quote = "Reduces rendering time by 40%",
        string? title = "Vendor pricing") =>
        new()
        {
            Url = url,
            Quote = quote,
            Title = title,
            Verified = verified,
            SourceRights = rights,
            PageId = "page-1",
            RunId = "run-1",
        };

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Verified_rights_cleared_citation_is_emitted_with_url_and_quote()
    {
        var merged = GccV2JsonLdBuilder.MergeCitations(
            ArticleJsonLd,
            [Citation("https://vendor.example/pricing")]);

        var citations = Parse(merged)["citation"]!.AsArray();
        var emitted = citations
            .OfType<JsonObject>()
            .Single(n => (string?)n["url"] == "https://vendor.example/pricing");

        Assert.Equal("WebPage", (string?)emitted["@type"]);
        Assert.Equal("Vendor pricing", (string?)emitted["name"]);
        Assert.Equal("Reduces rendering time by 40%", (string?)emitted["description"]);
    }

    [Fact]
    public void Existing_v1_companion_cross_link_is_preserved_not_replaced()
    {
        var merged = GccV2JsonLdBuilder.MergeCitations(
            ArticleJsonLd,
            [Citation("https://vendor.example/pricing")]);

        var citations = Parse(merged)["citation"]!.AsArray();

        // v1's companion BlogPosting link must survive alongside the new source attribution.
        Assert.Contains(citations.OfType<JsonObject>(),
            n => (string?)n["url"] == "https://example.com/companion");
        Assert.Equal(2, citations.Count);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("prohibited")]
    [InlineData(null)]
    [InlineData("")]
    public void Citation_without_cleared_rights_is_never_emitted(string? rights)
    {
        var merged = GccV2JsonLdBuilder.MergeCitations(
            ArticleJsonLd,
            [Citation("https://vendor.example/pricing", rights: rights)]);

        var citations = Parse(merged)["citation"]!.AsArray();
        Assert.DoesNotContain(citations.OfType<JsonObject>(),
            n => (string?)n["url"] == "https://vendor.example/pricing");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void Unverified_citation_is_never_emitted(bool? verified)
    {
        var merged = GccV2JsonLdBuilder.MergeCitations(
            ArticleJsonLd,
            [Citation("https://vendor.example/pricing", verified: verified)]);

        var citations = Parse(merged)["citation"]!.AsArray();
        Assert.DoesNotContain(citations.OfType<JsonObject>(),
            n => (string?)n["url"] == "https://vendor.example/pricing");
    }

    [Fact]
    public void Licensed_rights_are_accepted_alongside_consented()
    {
        var merged = GccV2JsonLdBuilder.MergeCitations(
            ArticleJsonLd,
            [Citation("https://vendor.example/licensed", rights: "licensed")]);

        var citations = Parse(merged)["citation"]!.AsArray();
        Assert.Contains(citations.OfType<JsonObject>(),
            n => (string?)n["url"] == "https://vendor.example/licensed");
    }

    [Fact]
    public void Duplicate_source_urls_are_emitted_once()
    {
        var merged = GccV2JsonLdBuilder.MergeCitations(
            ArticleJsonLd,
            [
                Citation("https://vendor.example/pricing"),
                Citation("https://vendor.example/pricing", quote: "A different quote, same page"),
            ]);

        var citations = Parse(merged)["citation"]!.AsArray();
        Assert.Single(
            citations.OfType<JsonObject>(),
            n => (string?)n["url"] == "https://vendor.example/pricing");
    }

    [Fact]
    public void No_emittable_citations_leaves_document_byte_identical()
    {
        var merged = GccV2JsonLdBuilder.MergeCitations(
            ArticleJsonLd,
            [Citation("https://vendor.example/pricing", rights: "prohibited")]);

        Assert.Equal(ArticleJsonLd, merged);
    }

    [Fact]
    public void Citations_attach_to_primary_node_not_faq_node_inside_graph()
    {
        // Mirrors MergeFaqPage output: primary article + FAQPage in an @graph.
        const string graphJsonLd = """
            {
              "@context": "https://schema.org",
              "@graph": [
                { "@type": "TechArticle", "headline": "Tool review" },
                { "@type": "FAQPage", "mainEntity": [] }
              ]
            }
            """;

        var merged = GccV2JsonLdBuilder.MergeCitations(
            graphJsonLd,
            [Citation("https://vendor.example/pricing")]);

        var graph = Parse(merged)["@graph"]!.AsArray();
        var article = graph.OfType<JsonObject>().Single(n => (string?)n["@type"] == "TechArticle");
        var faq = graph.OfType<JsonObject>().Single(n => (string?)n["@type"] == "FAQPage");

        Assert.NotNull(article["citation"]);
        Assert.Null(faq["citation"]);
    }

    [Fact]
    public void Malformed_primary_json_is_returned_unchanged_rather_than_throwing()
    {
        const string notJson = "{ this is not valid json";

        var merged = GccV2JsonLdBuilder.MergeCitations(
            notJson,
            [Citation("https://vendor.example/pricing")]);

        Assert.Equal(notJson, merged);
    }

    [Fact]
    public void Citation_without_url_is_skipped()
    {
        var merged = GccV2JsonLdBuilder.MergeCitations(
            ArticleJsonLd,
            [Citation("")]);

        Assert.Equal(ArticleJsonLd, merged);
    }
}
