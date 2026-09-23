using System.Text.Json;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.Workflow.SchemaBuilders;

/// <summary>
/// Stage 9 verification, binary per the plan: areaServed present and non-empty only when the crawl
/// summary has one; publisher @type mirrors what the crawled site declared; FAQPage present only
/// when the document actually has an FAQ section.
/// </summary>
public class SchemaAreaServedPublisherTypeFaqTests
{
    private static ContentMetadata Metadata(
        IReadOnlyList<string>? areaServed = null,
        string? publisherType = null,
        IReadOnlyList<ContentFaqEntry>? faq = null) => new(
        Headline: "Headline",
        Description: "Description",
        AuthorName: "Author",
        PublisherName: "Geek",
        PublisherLogoUrl: "https://geek.test/logo.png",
        CanonicalUrl: "https://geek.test/a",
        MainImageUrl: "https://geek.test/img.png",
        DatePublishedUtc: DateTime.UtcNow,
        DateModifiedUtc: DateTime.UtcNow,
        Keywords: ["ai"],
        WordCount: 500,
        AreaServed: areaServed,
        PublisherType: publisherType,
        Faq: faq);

    // --- areaServed: present only when non-empty, never emitted as [] ---

    [Fact]
    public void AreaServed_absent_from_publisher_when_the_summary_has_none()
    {
        var builder = new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder());
        var json = builder.Build(Metadata(areaServed: null), "https://geek.test/blog");

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("publisher").TryGetProperty("areaServed", out _));
    }

    [Fact]
    public void AreaServed_present_and_matches_when_the_summary_has_one()
    {
        var builder = new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder());
        var json = builder.Build(Metadata(areaServed: ["Denver, CO", "Boulder, CO"]), "https://geek.test/blog");

        using var doc = JsonDocument.Parse(json);
        var areaServed = doc.RootElement.GetProperty("publisher").GetProperty("areaServed")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["Denver, CO", "Boulder, CO"], areaServed);
    }

    [Fact]
    public void AreaServed_is_never_emitted_as_an_empty_array()
    {
        // An empty [] asserts "serves nowhere" -- worse than omitting the property.
        var builder = new ArticleSchemaBuilder(new SoftwareApplicationSchemaBuilder());
        var json = builder.Build(Metadata(areaServed: []), "https://geek.test/blog");

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("publisher").TryGetProperty("areaServed", out _));
    }

    // --- publisher @type: mirrors the declared type, defaults to Organization ---

    [Theory]
    [InlineData("LocalBusiness")]
    [InlineData("ProfessionalService")]
    public void Publisher_type_mirrors_what_the_crawled_site_declared(string declaredType)
    {
        var builder = new BlogPostingSchemaBuilder();
        var json = builder.Build(Metadata(publisherType: declaredType), "https://geek.test/a");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(declaredType, doc.RootElement.GetProperty("publisher").GetProperty("@type").GetString());
    }

    [Fact]
    public void Publisher_type_defaults_to_Organization_when_nothing_was_declared()
    {
        var builder = new BlogPostingSchemaBuilder();
        var json = builder.Build(Metadata(publisherType: null), "https://geek.test/a");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Organization", doc.RootElement.GetProperty("publisher").GetProperty("@type").GetString());
    }

    // --- FAQPage: present only when the document actually has FAQ content ---

    [Fact]
    public void FaqPage_absent_when_the_document_has_no_faq_section()
    {
        var builder = new BlogPostingSchemaBuilder();
        var json = builder.Build(Metadata(faq: null), "https://geek.test/a");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("BlogPosting", doc.RootElement.GetProperty("@type").GetString());
        Assert.False(doc.RootElement.TryGetProperty("@graph", out _));
    }

    [Fact]
    public void FaqPage_present_with_question_and_answer_when_the_document_has_faq_content()
    {
        var builder = new BlogPostingSchemaBuilder();
        var json = builder.Build(
            Metadata(faq: [new ContentFaqEntry("What is RAG?", "Retrieval and verification.")]),
            "https://geek.test/a");

        using var doc = JsonDocument.Parse(json);
        var graph = doc.RootElement.GetProperty("@graph").EnumerateArray().ToList();
        var faqNode = graph.Single(n => n.GetProperty("@type").GetString() == "FAQPage");
        var question = faqNode.GetProperty("mainEntity")[0];
        Assert.Equal("Question", question.GetProperty("@type").GetString());
        Assert.Equal("What is RAG?", question.GetProperty("name").GetString());
        Assert.Equal("Answer", question.GetProperty("acceptedAnswer").GetProperty("@type").GetString());
        Assert.Equal("Retrieval and verification.", question.GetProperty("acceptedAnswer").GetProperty("text").GetString());
    }

    [Fact]
    public void SoftwareApplication_tool_page_also_omits_and_includes_faq_correctly()
    {
        var builder = new SoftwareApplicationSchemaBuilder();
        var app = new SoftwareApplicationDescriptor("Tool", "Does things");

        var withoutFaq = builder.BuildToolPage(Metadata(faq: null), "https://geek.test/pillar", app);
        using var noFaqDoc = JsonDocument.Parse(withoutFaq);
        Assert.Equal("SoftwareApplication", noFaqDoc.RootElement.GetProperty("@type").GetString());

        var withFaq = builder.BuildToolPage(
            Metadata(faq: [new ContentFaqEntry("Q", "A")]), "https://geek.test/pillar", app);
        using var faqDoc = JsonDocument.Parse(withFaq);
        var graph = faqDoc.RootElement.GetProperty("@graph").EnumerateArray().ToList();
        Assert.Contains(graph, n => n.GetProperty("@type").GetString() == "FAQPage");
        Assert.Contains(graph, n => n.GetProperty("@type").GetString() == "SoftwareApplication");
    }

    // --- ExtractFaqPairs: the document-walking extractor itself ---

    [Fact]
    public void ExtractFaqPairs_reads_h3_children_of_an_FAQ_section_as_question_and_answer()
    {
        var document = new ContentDocument(
            Lede: new Section("h2", "Intro", [new TextParagraph([new Run("Body.")])], null, []),
            Sections:
            [
                new Section("h2", "FAQ", [], null,
                [
                    new Section("h3", "What is RAG?",
                        [new TextParagraph([new Run("Retrieval and verification.")])], null, []),
                    new Section("h3", "Does it generate?",
                        [new TextParagraph([new Run("No.")])], null, []),
                ]),
            ]);

        var pairs = ContentDocumentText.ExtractFaqPairs(document);

        Assert.Equal(2, pairs.Count);
        Assert.Equal("What is RAG?", pairs[0].Question);
        Assert.Equal("Retrieval and verification.", pairs[0].Answer);
    }

    [Fact]
    public void ExtractFaqPairs_returns_empty_when_there_is_no_faq_section()
    {
        var document = new ContentDocument(
            Lede: new Section("h2", "Intro", [new TextParagraph([new Run("Body.")])], null, []),
            Sections: [new Section("h2", "Overview", [new TextParagraph([new Run("Body.")])], null, [])]);

        Assert.Empty(ContentDocumentText.ExtractFaqPairs(document));
    }

    [Fact]
    public void ExtractFaqPairs_matches_a_People_Also_Ask_heading_too()
    {
        var document = new ContentDocument(
            Lede: new Section("h2", "Intro", [new TextParagraph([new Run("Body.")])], null, []),
            Sections:
            [
                new Section("h2", "People Also Ask", [], null,
                [
                    new Section("h3", "Is this free?", [new TextParagraph([new Run("Yes.")])], null, []),
                ]),
            ]);

        var pairs = ContentDocumentText.ExtractFaqPairs(document);

        Assert.Single(pairs);
        Assert.Equal("Is this free?", pairs[0].Question);
    }
}
