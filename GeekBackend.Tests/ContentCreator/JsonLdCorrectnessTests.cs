using System.Text.Json;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The published JSON+LD, checked against what it actually claims.
///
/// Jeff pasted a live document on 2026-09-23 — a TechArticle plus four SoftwareApplications — and
/// said "json+ld is wrong". It was, in five separate ways, and every one of them was a false or
/// meaningless assertion rather than a missing nicety.
/// </summary>
public class JsonLdCorrectnessTests
{
    private static ContentMetadata Metadata(string canonical = "https://geek.test/use-cases/marketing/ap") =>
        ContentMetadataFactory.For(
            Context(),
            "Automated Accounts Payable",
            "How automated AP works.",
            canonical,
            ["ap automation"],
            ContentDocumentText.FromPlainText("Body text."));

    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "ap automation",
        Department: "marketing",
        SiteName: "Acme",
        DetectedTone: string.Empty,
        DetectedFocus: string.Empty,
        CrawledHeadings: [],
        CrawledParagraphs: [],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: "Geek At Your Spot",
        PublisherLogoUrl: "https://geek.test/images/GeekAtYourSpot.svg",
        AuthorName: "Geek At Your Spot Editorial Team",
        ArticleBaseUrl: "https://geek.test/articles",
        BlogBaseUrl: "https://geek.test/blog",
        ToolBaseUrl: "https://geek.test/tools",
        ImplementerPositioning: "an AI implementation partner",
        Provider: GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi);

    private static ArticleSchemaBuilder Article() => new(new SoftwareApplicationSchemaBuilder());

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement NodeOfType(JsonElement root, string type) =>
        root.GetProperty("@graph").EnumerateArray()
            .First(n => n.GetProperty("@type").GetString() == type);

    [Fact]
    public void TheArticleImageIsAPlaceholderNotTheCompanyLogo()
    {
        // Published markup declared the same logo SVG as every article's image, because the factory
        // handed PublisherLogoUrl to the MainImageUrl parameter two slots later. The pipeline makes
        // an image *prompt*, so the real URL is genuinely unknown at this point.
        var json = Parse(Article().Build(Metadata(), relatedBlogPostUrl: string.Empty));

        var image = json.GetProperty("image").EnumerateArray().Single().GetString();
        Assert.Equal(ContentMetadataFactory.ArticleImagePlaceholder, image);

        // The logo is still the logo -- it belongs on publisher.logo and nowhere else. The defect
        // was one value doing both jobs, not the value existing.
        Assert.Equal(
            "https://geek.test/images/GeekAtYourSpot.svg",
            json.GetProperty("publisher").GetProperty("logo").GetProperty("url").GetString());
    }

    [Fact]
    public void TheAuthorIsAnOrganizationNotAPerson()
    {
        // "Geek At Your Spot Editorial Team" typed as a Person resolves to no entity at all.
        var json = Parse(Article().Build(Metadata(), relatedBlogPostUrl: string.Empty));

        Assert.Equal("Organization", json.GetProperty("author").GetProperty("@type").GetString());
    }

    [Fact]
    public void ASoftwareApplicationUrlIsTheProductsOwnHomeAndOurPageIsMainEntityOfPage()
    {
        // The published document said Tipalti is located at geekatyourspot.com.
        var app = new SoftwareApplicationDescriptor(
            "Tipalti", "Payables automation.",
            Url: "https://tipalti.com",
            PageUrl: "https://geek.test/tools/marketing/tipalti");

        var json = Parse(Article().Build(Metadata(), string.Empty, [app]));
        var node = NodeOfType(json, "SoftwareApplication");

        Assert.Equal("https://tipalti.com", node.GetProperty("url").GetString());
        Assert.Equal(
            "https://geek.test/tools/marketing/tipalti",
            node.GetProperty("mainEntityOfPage").GetProperty("@id").GetString());
    }

    [Fact]
    public void AProductWithNoKnownHomeGetsNoUrlRatherThanOurs()
    {
        // The orchestrator has no vendor domain on a GeneratedContent row. Absent beats wrong.
        var app = new SoftwareApplicationDescriptor(
            "Rillion", "AP automation.", Url: null, PageUrl: "https://geek.test/tools/marketing/rillion");

        var node = NodeOfType(Parse(Article().Build(Metadata(), string.Empty, [app])), "SoftwareApplication");

        Assert.False(node.TryGetProperty("url", out _));
        Assert.Equal(
            "https://geek.test/tools/marketing/rillion",
            node.GetProperty("mainEntityOfPage").GetProperty("@id").GetString());
    }

    [Fact]
    public void TheGraphSaysHowItsNodesRelate()
    {
        // Five nodes, no @id, nothing connecting them: an article, and separately four products.
        var apps = new[]
        {
            new SoftwareApplicationDescriptor("Tipalti", "x", Url: "https://tipalti.com"),
            new SoftwareApplicationDescriptor("Medius", "x", Url: "https://www.medius.com"),
        };

        var json = Parse(Article().Build(Metadata(), string.Empty, apps));
        var article = NodeOfType(json, "Article");

        Assert.Equal("https://geek.test/use-cases/marketing/ap#article", article.GetProperty("@id").GetString());

        var mentioned = article.GetProperty("mentions").EnumerateArray()
            .Select(m => m.GetProperty("@id").GetString())
            .ToList();
        Assert.Equal(["https://tipalti.com", "https://www.medius.com"], mentioned);

        // Every id the article mentions resolves to a node actually in the graph.
        var ids = json.GetProperty("@graph").EnumerateArray()
            .Where(n => n.TryGetProperty("@id", out _))
            .Select(n => n.GetProperty("@id").GetString())
            .ToHashSet();
        Assert.All(mentioned, id => Assert.Contains(id, ids));
    }

    [Fact]
    public void OurOwnCompanionPageIsARelatedLinkNotACitation()
    {
        // "citation" means a work this page cites. Our own blog is a sibling in the same cluster,
        // and calling it a citation quietly claims external corroboration we do not have.
        var json = Parse(Article().Build(Metadata(), "https://geek.test/blog/marketing/why-ap"));

        Assert.False(json.TryGetProperty("citation", out _));
        Assert.Equal("https://geek.test/blog/marketing/why-ap", json.GetProperty("relatedLink").GetString());
    }

    [Fact]
    public void TheBlogCarriesTheSameCorrections()
    {
        var json = Parse(new BlogPostingSchemaBuilder()
            .Build(Metadata("https://geek.test/blog/marketing/why-ap"), "https://geek.test/use-cases/marketing/ap"));

        Assert.Equal("Organization", json.GetProperty("author").GetProperty("@type").GetString());
        Assert.Equal(ContentMetadataFactory.ArticleImagePlaceholder,
            json.GetProperty("image").EnumerateArray().Single().GetString());
        Assert.Equal("https://geek.test/blog/marketing/why-ap#article", json.GetProperty("@id").GetString());
        Assert.False(json.TryGetProperty("citation", out _));
        Assert.Equal("https://geek.test/use-cases/marketing/ap", json.GetProperty("relatedLink").GetString());
    }

    [Fact]
    public void APillarIsAnArticleNotTechnicalDocumentation()
    {
        // TechArticle is schema.org's type for how-to tasks, procedures, troubleshooting and
        // specifications. A commercial pillar written for buyers is none of those, and
        // proficiencyLevel is defined only on TechArticle -- hardcoded "Beginner" on every page.
        var json = Parse(Article().Build(Metadata(), relatedBlogPostUrl: string.Empty));

        Assert.Equal("Article", json.GetProperty("@type").GetString());
        Assert.False(json.TryGetProperty("proficiencyLevel", out _));
    }

    [Fact]
    public void AToolPagePointsAtThePillarByThePillarsActualType()
    {
        // subjectOf naming a type the target does not have is a dangling reference.
        var about = new SoftwareApplicationDescriptor("Medius", "AP automation.", Url: "https://www.medius.com");

        var json = Parse(new SoftwareApplicationSchemaBuilder().BuildToolPage(
            Metadata("https://geek.test/tools/marketing/medius"),
            "https://geek.test/use-cases/marketing/ap",
            about));

        Assert.Equal("Article", json.GetProperty("subjectOf").GetProperty("@type").GetString());
    }

    [Fact]
    public void TheToolPageDoesNotOverwriteTheProductsUrlWithOurs()
    {
        var about = new SoftwareApplicationDescriptor(
            "Medius", "AP automation.",
            Url: "https://www.medius.com",
            PageUrl: "https://geek.test/tools/marketing/medius");

        var json = Parse(new SoftwareApplicationSchemaBuilder().BuildToolPage(
            Metadata("https://geek.test/tools/marketing/medius"),
            "https://geek.test/use-cases/marketing/ap",
            about));

        Assert.Equal("https://www.medius.com", json.GetProperty("url").GetString());
        Assert.Equal("Organization", json.GetProperty("author").GetProperty("@type").GetString());
        Assert.Equal(
            "https://geek.test/use-cases/marketing/ap",
            json.GetProperty("subjectOf").GetProperty("@id").GetString());
    }
}
