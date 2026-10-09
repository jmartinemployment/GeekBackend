using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// <see cref="GccToolFaqEvidence"/> -- per-batch evidence for the tool page's operator FAQ,
/// read from the partner pages this create already retrieved (<c>partnerPages</c>), rather than
/// the page-level block built for the lede/body. See the fix this covers:
/// <c>GccGenerateService.ToolFaqAsync</c> previously answered every FAQ batch from
/// <c>toolOutlineCtx.EvidenceBlock</c>, so a question whose topic the page-level query never
/// needed was reported "left out" even when partnerPages carried a paragraph that answered it.
/// </summary>
public class GccToolFaqEvidenceTests
{
    private static GccQuoteablePage Page(string title, string url, params string[] paragraphs) =>
        new(Url: url, Title: title, Headings: [], Paragraphs: paragraphs);

    [Fact]
    public void No_partner_pages_at_all_returns_nothing()
    {
        var evidence = GccToolFaqEvidence.BuildFor([], ["Does it sync with QuickBooks?"]);
        Assert.Equal(string.Empty, evidence);
    }

    [Fact]
    public void No_question_in_the_batch_returns_nothing()
    {
        var pages = new[] { Page("Widget", "https://partner.test/widget", "Widget syncs every payment to QuickBooks Online.") };
        var evidence = GccToolFaqEvidence.BuildFor(pages, []);
        Assert.Equal(string.Empty, evidence);
    }

    [Fact]
    public void A_question_whose_topic_the_page_level_block_never_needed_is_still_answered_from_partner_pages()
    {
        // This is the exact shape of the original bug: the page-level EvidenceBlock is built for
        // the page's own keyword/angle and never mentions "Customer Billing Portal" at all, but
        // partnerPages -- the full retrieved pool -- has a paragraph about it. The old code passed
        // the page-level block (which would be empty or irrelevant here) to every batch; the fix
        // searches partnerPages directly, so this question is no longer reported "left out".
        var pages = new[]
        {
            Page(
                "Widget Integrations",
                "https://partner.test/integrations",
                "The Widget Customer Billing Portal interfaces directly with cash forecasting, letting customers see upcoming invoices."),
            Page(
                "Widget Pricing",
                "https://partner.test/pricing",
                "Widget pricing starts at $99 per month for small teams."),
        };

        var evidence = GccToolFaqEvidence.BuildFor(
            pages, ["How does the Customer Billing Portal interface with cash forecasting?"]);

        Assert.Contains("[Widget Integrations] (https://partner.test/integrations)", evidence, StringComparison.Ordinal);
        Assert.Contains("Customer Billing Portal interfaces directly with cash forecasting", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("Widget Pricing", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_question_with_no_matching_paragraph_anywhere_returns_nothing_rather_than_a_near_miss()
    {
        var pages = new[]
        {
            Page("Widget Pricing", "https://partner.test/pricing", "Widget pricing starts at $99 per month."),
        };

        // Shares only stop words and short words with the page ("it", "to", "a") -- no real topic
        // overlap, so this must come back empty, not latch onto the pricing paragraph by accident.
        var evidence = GccToolFaqEvidence.BuildFor(pages, ["Can it fly to the moon?"]);

        Assert.Equal(string.Empty, evidence);
    }

    [Fact]
    public void Matching_is_whole_word_not_substring()
    {
        // "cash" must not match inside "cashier" -- a retail staffing paragraph is not evidence for
        // a cash-flow-forecast question just because one word contains the other as a substring.
        // Deliberately shares no other significant word with the question (no product name in
        // either), so the only way this could score above zero is the substring false match.
        var pages = new[]
        {
            Page(
                "Staffing Guide",
                "https://partner.test/staffing",
                "Every cashier terminal here processes barcode scans for retail checkout lines."),
        };

        var evidence = GccToolFaqEvidence.BuildFor(pages, ["Does the platform support cash flow forecasting?"]);

        Assert.Equal(string.Empty, evidence);
    }

    [Fact]
    public void Higher_overlap_paragraphs_are_kept_over_lower_overlap_ones_when_the_cap_is_reached()
    {
        var strong = Page(
            "Widget Forecasting",
            "https://partner.test/forecasting",
            "Widget's cash flow forecast uses AI models trained on historical invoice data to predict collections.");
        var weak = Page(
            "Widget Blog",
            "https://partner.test/blog",
            "Widget's data helps teams forecast workloads across many unrelated projects every single day.");

        // weak shares exactly two significant words with the question ("data", "forecast");
        // strong shares six ("cash", "flow", "forecast", "historical", "invoice", "data"). Both
        // score above zero, so both are kept -- this asserts the higher-overlap page is ranked,
        // and therefore rendered, ahead of the lower-overlap one rather than in list order.
        var evidence = GccToolFaqEvidence.BuildFor(
            [weak, strong],
            ["Can the AI cash flow forecast be adjusted using historical invoice data?"]);

        var strongIndex = evidence.IndexOf("Widget Forecasting", StringComparison.Ordinal);
        var weakIndex = evidence.IndexOf("Widget Blog", StringComparison.Ordinal);
        Assert.True(strongIndex >= 0);
        Assert.True(weakIndex >= 0);
        Assert.True(strongIndex < weakIndex);
    }

    [Fact]
    public void Multiple_matching_pages_are_each_grouped_under_their_own_title_and_url()
    {
        var pages = new[]
        {
            Page("Widget Security", "https://partner.test/security", "Widget encrypts every stored payment credential."),
            Page("Widget Compliance", "https://partner.test/compliance", "Widget is SOC 2 compliant for payment credential storage."),
        };

        var evidence = GccToolFaqEvidence.BuildFor(pages, ["How does Widget secure stored payment credentials?"]);

        Assert.Contains("[Widget Security] (https://partner.test/security)", evidence, StringComparison.Ordinal);
        Assert.Contains("[Widget Compliance] (https://partner.test/compliance)", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendered_shape_matches_the_partner_evidence_block_the_prompt_already_expects()
    {
        var pages = new[]
        {
            Page("Partner Widget", "https://partner.test/widget", "Partner Widget syncs every payment to QuickBooks Online."),
        };

        var evidence = GccToolFaqEvidence.BuildFor(pages, ["Does it sync with QuickBooks Online?"]);

        Assert.Equal(
            "[Partner Widget] (https://partner.test/widget)\n  - Partner Widget syncs every payment to QuickBooks Online.",
            evidence);
    }
}
