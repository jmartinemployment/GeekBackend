using GeekAPI.Services.ContentCreatorV2.Publish;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2JsonLdFaqPageTests
{
    [Fact]
    public void ExtractFaqPairs_reads_people_also_ask_children()
    {
        var document = new ContentDocument(
            new Section("h2", "Lede", [new TextParagraph([new Run("Intro.")])], null, []),
            [
                new Section(
                    "h2",
                    "People Also Ask",
                    [],
                    null,
                    [
                        new Section(
                            "h3",
                            "What is reliability?",
                            [new TextParagraph([new Run("Reliability means repeatable outcomes.")])],
                            null,
                            []),
                        new Section(
                            "h3",
                            "Why does approval matter?",
                            [new TextParagraph([new Run("Approval gates prevent silent drift.")])],
                            null,
                            []),
                    ]),
            ]);

        var pairs = GccV2JsonLdBuilder.ExtractFaqPairs(document);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("What is reliability?", pairs[0].Question);
        Assert.Contains("repeatable outcomes", pairs[0].Answer);
    }

    [Fact]
    public void MergeFaqPage_wraps_single_primary_into_graph()
    {
        const string primary = """
            {
              "@context": "https://schema.org",
              "@type": "TechArticle",
              "headline": "Reliability"
            }
            """;

        var merged = GccV2JsonLdBuilder.MergeFaqPage(
            primary,
            [("What is reliability?", "Repeatable outcomes.")]);

        Assert.Contains("\"@graph\"", merged, StringComparison.Ordinal);
        Assert.Contains("FAQPage", merged, StringComparison.Ordinal);
        Assert.Contains("What is reliability?", merged, StringComparison.Ordinal);
        Assert.Contains("TechArticle", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void MergeFaqPage_appends_to_existing_graph()
    {
        const string primary = """
            {
              "@context": "https://schema.org",
              "@graph": [
                { "@type": "TechArticle", "headline": "Reliability" }
              ]
            }
            """;

        var merged = GccV2JsonLdBuilder.MergeFaqPage(
            primary,
            [("Why approve?", "To keep quality gates.")]);

        Assert.Contains("FAQPage", merged, StringComparison.Ordinal);
        Assert.Contains("Why approve?", merged, StringComparison.Ordinal);
        Assert.Contains("TechArticle", merged, StringComparison.Ordinal);
    }
}
