using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Which returned pages are not evidence, and which may not give a quotation. One definition, read by
/// the search boundary (<c>GccGroundingResolver</c>) and by the quotation list (<c>GccQuoteCandidates</c>).
/// </summary>
public class GccEvidencePagesTests
{
    private static GccQuoteablePage Page(string url, params string[] passages) => new(url, "A page", [], passages);

    [Theory]
    [InlineData("https://www.bill.com/legal/terms-of-service")]
    [InlineData("https://www.bill.com/privacy")]
    [InlineData("https://www.bill.com/terms")]
    [InlineData("https://www.bill.com/legal")]
    [InlineData("https://example.com/dpa/")]
    [InlineData("https://example.com/licenses.html")]
    [InlineData("https://example.com/privacy-policy")]
    [InlineData("https://example.com/terms-of-service/")]
    [InlineData("https://example.com/cookie-policy")]
    [InlineData("https://example.com/california-notice-at-collection/")]
    [InlineData("https://example.com/trust/sub-processors")]
    public void A_legal_document_is_not_evidence(string url)
    {
        Assert.True(GccEvidencePages.IsLegalDocument(url));
        Assert.Equal("a legal document", GccEvidencePages.WhyNotEvidence(Page(url, "Some text.")));
    }

    /// <summary>
    /// A question about a certification is answered from exactly such a page, so it stays evidence. A
    /// block quotation is how a tool solves the reader's problem, and is not cut from one.
    /// </summary>
    [Theory]
    [InlineData("https://www.bill.com/security")]
    [InlineData("https://www.bill.com/compliance/soc-2")]
    [InlineData("https://www.bill.com/careers")]
    [InlineData("https://www.bill.com/accessibility")]
    public void A_security_compliance_or_company_page_is_evidence_and_is_never_quoted(string url)
    {
        Assert.False(GccEvidencePages.IsLegalDocument(url));
        Assert.Null(GccEvidencePages.WhyNotEvidence(Page(url, "BILL is SOC 2 Type II certified.")));
        Assert.True(GccEvidencePages.IsNotQuotable(url));
    }

    [Theory]
    [InlineData("https://www.bill.com/product/accounts-receivable")]
    [InlineData("https://www.bill.com/product-updates")]
    [InlineData("https://example.com/legal-automation-software/")]
    [InlineData("https://example.com/blog/terms-every-ap-team-should-know")]
    [InlineData("https://example.com/gdpr-ready-invoicing")]
    public void A_product_page_is_evidence_and_may_be_quoted(string url)
    {
        Assert.Null(GccEvidencePages.WhyNotEvidence(Page(url, "What the product does.")));
        Assert.False(GccEvidencePages.IsNotQuotable(url));
    }

    [Fact]
    public void A_legal_document_is_never_quoted_either()
    {
        Assert.True(GccEvidencePages.IsNotQuotable("https://www.bill.com/legal/terms-of-service"));
    }

    /// <summary>bill.com/listicle on 2026-10-10: a page-builder component nobody finished, returned for two FAQ questions.</summary>
    [Fact]
    public void A_page_still_carrying_a_page_builders_default_text_is_not_evidence()
    {
        var stub = Page(
            "https://www.bill.com/listicle",
            "tups to mid-marketAvidXchangeBest for industry-specific solutionsThis is some text inside of a div block.FeaturesPros & cons");

        Assert.Equal("a template page still carrying placeholder text", GccEvidencePages.WhyNotEvidence(stub));
    }

    [Fact]
    public void One_passage_of_placeholder_text_is_enough_and_a_page_about_placeholders_is_not_caught()
    {
        Assert.True(GccEvidencePages.CarriesPlaceholderText(["Real text.", "lorem ipsum dolor sit amet, consectetur"]));
        Assert.False(GccEvidencePages.CarriesPlaceholderText(["Designers fill a mock-up with lorem ipsum until the copy is ready."]));
        Assert.False(GccEvidencePages.CarriesPlaceholderText([]));
    }

    [Fact]
    public void An_address_that_is_not_one_is_neither()
    {
        Assert.False(GccEvidencePages.IsLegalDocument(null));
        Assert.False(GccEvidencePages.IsLegalDocument("not a url"));
        Assert.False(GccEvidencePages.IsNotQuotable(""));
    }
}
