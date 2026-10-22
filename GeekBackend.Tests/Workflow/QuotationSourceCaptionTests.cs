using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services.Export;

namespace GeekBackend.Tests.Workflow;

/// <summary>
/// A block quotation's source is a link a reader can follow, not only a <c>cite</c> attribute nobody
/// sees: the quotation and its caption in a <c>figure</c>, which is where the HTML standard puts a
/// quotation's attribution. Jeff's skills ask for a named, linked source on a quotation as on a figure;
/// geekatyourspot's GEO audit of 2026-10-20 found tool pages naming a source and not linking it.
/// </summary>
public sealed class QuotationSourceCaptionTests
{
    private static string Render(QuoteParagraph quote) => SectionHtmlRenderer.RenderFragment(new ContentDocument(
        new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
        [new Section("h2", "Proof", [quote], null, [])]));

    [Fact]
    public void A_quotation_with_a_source_address_is_a_figure_whose_caption_links_the_source_by_host()
    {
        var html = Render(new QuoteParagraph([new Run("We cut approval time.")], Cite: "https://www.tipalti.test/ap"));

        Assert.Contains("<figure>", html, StringComparison.Ordinal);
        Assert.Contains("<blockquote cite=\"https://www.tipalti.test/ap\">", html, StringComparison.Ordinal);
        Assert.Contains("<p>We cut approval time.</p></blockquote>", html, StringComparison.Ordinal);
        Assert.Contains("<figcaption>Source: <a href=\"https://www.tipalti.test/ap\">tipalti.test</a></figcaption></figure>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quotation_with_no_source_is_a_bare_blockquote()
    {
        var html = Render(new QuoteParagraph([new Run("We cut approval time.")]));

        Assert.Contains("<blockquote><p>We cut approval time.</p></blockquote>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<figure", html, StringComparison.Ordinal);
        Assert.DoesNotContain("figcaption", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cite_that_is_not_an_address_is_kept_on_the_blockquote_and_gets_no_caption()
    {
        var html = Render(new QuoteParagraph([new Run("We cut approval time.")], Cite: "page 4"));

        Assert.Contains("<blockquote cite=\"page 4\"><p>We cut approval time.</p></blockquote>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<figure", html, StringComparison.Ordinal);
        Assert.DoesNotContain("figcaption", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_caption_is_text_in_the_document_and_the_source_is_linked_once()
    {
        var html = Render(new QuoteParagraph([new Run("We cut approval time.")], Cite: "https://tipalti.test/ap"));

        Assert.Equal(1, html.Split("<figcaption>").Length - 1);
        Assert.Equal(1, html.Split("href=\"https://tipalti.test/ap\"").Length - 1);
    }
}
