using System.Text.Json;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Every property we emit is legal on the type we emit it on.
///
/// <para>
/// This is the check that was missing. <c>JsonLdDeliveryTests</c> covers the envelope — that the
/// block reaches the page in a script tag, unescaped, unbreakable — and every one of its assertions
/// is about the wrapper rather than a single claim inside it. So markup that declared the company
/// logo as every article's image, named four vendors' products as living on our domain, and carried
/// <c>proficiencyLevel</c> on a type where it is not defined all shipped with a green test suite
/// beside it.
/// </para>
///
/// <para>
/// A property table is the mechanical version of that review: <c>proficiencyLevel</c> on an Article
/// fails here without anyone having to notice it. Deliberately test-side rather than a production
/// guard — an incomplete table should fail a build, never refuse a customer's generation.
/// </para>
/// </summary>
public class JsonLdPropertyLegalityTests
{
    /// <summary>
    /// Properties schema.org defines on each type we emit, narrowed to the ones these builders
    /// actually use. Adding an emitted property means adding it here, which is the point: the table
    /// is where "is this legal on this type?" gets asked once, in writing.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> Allowed = new(StringComparer.Ordinal)
    {
        // Article, and BlogPosting which is a subtype of it.
        ["Article"] = Props(
            "headline", "description", "image", "author", "publisher", "datePublished",
            "dateModified", "mainEntityOfPage", "keywords", "wordCount", "relatedLink",
            "citation", "mentions", "hasPart", "subjectOf", "url", "name", "inLanguage"),
        ["BlogPosting"] = Props(
            "headline", "description", "image", "author", "publisher", "datePublished",
            "dateModified", "mainEntityOfPage", "keywords", "wordCount", "relatedLink",
            "citation", "mentions", "hasPart", "subjectOf", "url", "name", "inLanguage"),
        // SoftwareApplication is a CreativeWork, so the article-shaped properties below are legal
        // on it -- but wordCount and proficiencyLevel are not, and neither is emitted.
        ["SoftwareApplication"] = Props(
            "name", "description", "url", "applicationCategory", "operatingSystem", "headline",
            "image", "author", "publisher", "datePublished", "dateModified", "mainEntityOfPage",
            "keywords", "subjectOf", "offers", "aggregateRating"),
        ["Organization"] = Props("name", "logo", "areaServed", "url"),
        ["Person"] = Props("name", "url"),
        ["ImageObject"] = Props("url", "width", "height"),
        ["WebPage"] = Props("url", "name"),
        ["FAQPage"] = Props("mainEntity", "name"),
        ["Question"] = Props("name", "acceptedAnswer", "text"),
        ["Answer"] = Props("text"),
    };

    /// <summary>JSON-LD keywords, legal on any node.</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "@context", "@type", "@id", "@graph",
    };

    private static HashSet<string> Props(params string[] names) => new(names, StringComparer.Ordinal);

    /// <summary>Walks every typed node in the document and reports each illegal property.</summary>
    private static List<string> IllegalProperties(JsonElement node, string path = "$")
    {
        var found = new List<string>();

        if (node.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in node.EnumerateArray())
            {
                found.AddRange(IllegalProperties(item, $"{path}[{i++}]"));
            }
            return found;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return found;
        }

        var type = node.TryGetProperty("@type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

        foreach (var property in node.EnumerateObject())
        {
            if (!Keywords.Contains(property.Name) && type is not null)
            {
                if (!Allowed.TryGetValue(type, out var allowed))
                {
                    found.Add($"{path}: unknown @type \"{type}\" -- add it to the table or stop emitting it.");
                }
                else if (!allowed.Contains(property.Name))
                {
                    found.Add($"{path}.{property.Name} is not defined on {type}.");
                }
            }

            found.AddRange(IllegalProperties(property.Value, $"{path}.{property.Name}"));
        }

        return found;
    }

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
        PublisherLogoUrl: "https://geek.test/images/logo.svg",
        AuthorName: "Geek At Your Spot Editorial Team",
        ArticleBaseUrl: "https://geek.test/articles",
        BlogBaseUrl: "https://geek.test/blog",
        ToolBaseUrl: "https://geek.test/tools",
        ImplementerPositioning: "an AI implementation partner",
        Provider: GeekAPI.Services.Workflow.Domain.Enums.LlmProviderType.OpenAi);

    private static ContentMetadata Metadata(string canonical) =>
        ContentMetadataFactory.For(
            Context(), "Automated Accounts Payable", "How automated AP works.", canonical,
            ["ap automation"],
            // A document with a real FAQ pair, so the FAQPage/Question/Answer nodes are exercised
            // too rather than silently skipped.
            FaqDocument());

    private static GeekAPI.Services.Workflow.Domain.Entities.ContentDocument FaqDocument()
    {
        var answer = new GeekAPI.Services.Workflow.Domain.Entities.Section(
            "h3", "Is it secure?",
            [new GeekAPI.Services.Workflow.Domain.Entities.TextParagraph(
                [new GeekAPI.Services.Workflow.Domain.Entities.Run("Yes, SOC 2 Type II certified.")])],
            null, []);
        var faq = new GeekAPI.Services.Workflow.Domain.Entities.Section(
            "h2", "People Also Ask", [], null, [answer]);
        return new GeekAPI.Services.Workflow.Domain.Entities.ContentDocument(
            new GeekAPI.Services.Workflow.Domain.Entities.Section("h2", "Opening", [], null, []),
            [faq]);
    }

    private static readonly SoftwareApplicationDescriptor Tipalti = new(
        "Tipalti", "Payables automation.",
        Url: "https://tipalti.com",
        PageUrl: "https://geek.test/tools/marketing/tipalti");

    public static TheoryData<string> EveryDocument => new() { "pillar", "blog", "tool" };

    private static string Build(string which)
    {
        var apps = new SoftwareApplicationSchemaBuilder();
        return which switch
        {
            "pillar" => new ArticleSchemaBuilder(apps).Build(
                Metadata("https://geek.test/use-cases/marketing/ap"),
                "https://geek.test/blog/marketing/why-ap",
                [Tipalti]),
            "blog" => new BlogPostingSchemaBuilder().Build(
                Metadata("https://geek.test/blog/marketing/why-ap"),
                "https://geek.test/use-cases/marketing/ap"),
            "tool" => apps.BuildToolPage(
                Metadata("https://geek.test/tools/marketing/tipalti"),
                "https://geek.test/use-cases/marketing/ap",
                Tipalti),
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "unknown document"),
        };
    }

    [Theory]
    [MemberData(nameof(EveryDocument))]
    public void EveryEmittedPropertyIsDefinedOnTheTypeItIsEmittedOn(string which)
    {
        var illegal = IllegalProperties(JsonDocument.Parse(Build(which)).RootElement);

        Assert.True(illegal.Count == 0, $"{which}:{Environment.NewLine}{string.Join(Environment.NewLine, illegal)}");
    }

    [Fact]
    public void TheTableActuallyRejectsSomething()
    {
        // A validator nothing can fail is a validator that proves nothing. proficiencyLevel on an
        // Article is the exact property that shipped on every pillar page.
        var doc = JsonDocument.Parse("""{"@type":"Article","headline":"x","proficiencyLevel":"Beginner"}""");

        var illegal = IllegalProperties(doc.RootElement);

        Assert.Single(illegal);
        Assert.Contains("proficiencyLevel is not defined on Article", illegal[0]);
    }

    [Fact]
    public void AnUnknownTypeIsReportedRatherThanWavedThrough()
    {
        var doc = JsonDocument.Parse("""{"@type":"TechArticle","headline":"x"}""");

        Assert.Contains(IllegalProperties(doc.RootElement), m => m.Contains("unknown @type \"TechArticle\""));
    }
}
