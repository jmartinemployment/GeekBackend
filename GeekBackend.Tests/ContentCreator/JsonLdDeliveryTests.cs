using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services.Export;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// JSON-LD has to ship as &lt;script type="application/ld+json"&gt;. Nothing else counts: an
/// entity-encoded blob in a meta content attribute is inert to every consumer of structured data,
/// so the markup can be perfectly formed and still earn nothing.
///
/// Written because a live page was found delivering its graph as
/// &lt;meta name="script:ld+json" content="{&amp;quot;@context&amp;quot;..."&gt; -- a well-formed
/// TechArticle with five SoftwareApplication nodes, invisible to all of it. That mangling happens
/// downstream of this renderer, which does the right thing; these tests exist so it stays that way
/// on this side of the boundary (Jeff, 2026-09-23: "I do not want to inherit that mistake").
/// </summary>
public class JsonLdDeliveryTests
{
    private static ContentDocument Doc() => new(
        new Section("h2", "A hook", [new TextParagraph([new Run("Opening.")])], null, [], null),
        [new Section("h2", "A section", [new TextParagraph([new Run("Body.")])], null, [], null)]);

    private static string Render(string jsonLd) => SectionHtmlRenderer.RenderDocument(
        title: "A Title",
        description: "A description.",
        canonicalUrl: "https://example.com/a",
        ogType: "article",
        ogImage: null,
        jsonLdSchema: jsonLd,
        additionalMeta: new Dictionary<string, string?>(),
        body: Doc());

    [Fact]
    public void SchemaShipsInAScriptTagAndNeverAsMeta()
    {
        var html = Render("""{"@context":"https://schema.org","@type":"TechArticle"}""");

        Assert.Contains("<script type=\"application/ld+json\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("script:ld+json", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SchemaIsNotEntityEncoded()
    {
        var html = Render("""{"@context":"https://schema.org","@type":"TechArticle"}""");

        // A parser reads the script body as JSON, so its quotes must be literal. &quot; here is the
        // exact defect seen on the live page.
        Assert.Contains("\"@context\":\"https://schema.org\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("&quot;@context&quot;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AClosingScriptInsideAStringCannotBreakOutOfTheTag()
    {
        var html = Render("""{"@type":"TechArticle","headline":"</script><img src=x>"}""");

        Assert.DoesNotContain("</script><img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<\\/script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyGraphEmitsNoScriptAtAll()
    {
        // Better nothing than an empty structured-data block asserting a page has none.
        Assert.DoesNotContain("application/ld+json", Render("{}"), StringComparison.Ordinal);
        Assert.DoesNotContain("application/ld+json", Render("  "), StringComparison.Ordinal);
    }
}
