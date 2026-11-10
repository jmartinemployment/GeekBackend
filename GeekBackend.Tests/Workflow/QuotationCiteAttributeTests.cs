using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services.Export;

namespace GeekBackend.Tests.Workflow;

/// <summary>
/// A block quotation is a <c>blockquote</c> carrying its source on the <c>cite</c> attribute, and
/// nothing around it. From 2026-10-22 to 2026-10-26 the renderer wrapped a quotation whose cite was an
/// address in a <c>figure</c> with a "Source:" caption under it; Jeff, 2026-10-26: "I have no figure;
/// typically a figure is an image, and that is not the implementation ... please remove the erroneous
/// &lt;figure&gt; tag." Whose words they are is the writer's sentence before the quotation.
/// </summary>
public sealed class QuotationCiteAttributeTests
{
    private static string Render(QuoteParagraph quote) => SectionHtmlRenderer.RenderFragment(new ContentDocument(
        new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
        [new Section("h2", "Proof", [quote], null, [])]));

    [Fact]
    public void A_quotation_with_a_source_address_is_a_blockquote_with_that_cite_and_nothing_around_it()
    {
        var html = Render(new QuoteParagraph([new Run("We cut approval time.")], Cite: "https://www.tipalti.test/ap"));

        Assert.Contains(
            "<blockquote cite=\"https://www.tipalti.test/ap\"><p>We cut approval time.</p></blockquote>",
            html, StringComparison.Ordinal);
        Assert.DoesNotContain("<figure", html, StringComparison.Ordinal);
        Assert.DoesNotContain("figcaption", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Source:", html, StringComparison.Ordinal);
        // The address appears once, as the attribute, never as a link in the text.
        Assert.Equal(1, html.Split("https://www.tipalti.test/ap").Length - 1);
        Assert.DoesNotContain("href=\"https://www.tipalti.test/ap\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quotation_with_no_source_is_a_bare_blockquote()
    {
        var html = Render(new QuoteParagraph([new Run("We cut approval time.")]));

        Assert.Contains("<blockquote><p>We cut approval time.</p></blockquote>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<figure", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cite_that_is_not_an_address_is_kept_on_the_blockquote_as_it_is()
    {
        var html = Render(new QuoteParagraph([new Run("We cut approval time.")], Cite: "page 4"));

        Assert.Contains("<blockquote cite=\"page 4\"><p>We cut approval time.</p></blockquote>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<figure", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cite_is_html_encoded()
    {
        var html = Render(new QuoteParagraph([new Run("Quoted.")], Cite: "https://tipalti.test/ap?a=1&b=\"2\""));

        Assert.Contains("<blockquote cite=\"https://tipalti.test/ap?a=1&amp;b=&quot;2&quot;\">", html, StringComparison.Ordinal);
    }
}
