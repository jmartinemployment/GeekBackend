using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// What a block quotation may be chosen from.
///
/// <para>
/// Candidates are cut here so the model never retypes a sentence — it answers with a number, and
/// the text is read back from this list. Everything these tests pin is therefore load-bearing: a
/// bad candidate cannot be caught later, because there is no later check.
/// </para>
/// </summary>
public class GccQuoteCandidatesTests
{
    private const string Url = "https://stampli.com/case-studies/cti/";

    private static GccQuoteablePage Page(string url, params string[] paragraphs) =>
        new(url, "CTI case study", [], paragraphs);

    [Fact]
    public void A_complete_sentence_becomes_a_candidate_carrying_its_page()
    {
        var pages = new[] { Page(Url, "Stampli plays an important part of our daily AP process every week.") };

        var candidates = GccQuoteCandidates.From(pages);

        var only = Assert.Single(candidates);
        Assert.Equal(1, only.Id);
        Assert.Equal(Url, only.PageUrl);
        Assert.StartsWith("Stampli plays", only.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rendered_chunk_labels_are_stripped_before_anything_is_cut()
    {
        // A retrieved "paragraph" is RenderChunk output, not prose. Without stripping, a candidate
        // reads "Section: Why Stampli" and gets quoted on a partner's advertisement.
        var pages = new[]
        {
            Page(Url,
                "Section: Why Stampli\n"
                + "Target Entity Match: Stampli\n"
                + "Specific detail: Stampli plays an important part of our daily AP process.\n"
                + "Linked from this section: pricing, demo, contact sales, about us"),
        };

        var candidates = GccQuoteCandidates.From(pages);

        // Asserted on the start of the text, not merely on the absence of one label. Checking
        // DoesNotContain("Section:") passed while the label was still attached, because the line
        // that survived the length floor carried "Specific detail:" instead.
        var only = Assert.Single(candidates);
        Assert.StartsWith("Stampli plays", only.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(":", only.Text[..20], StringComparison.Ordinal);
        Assert.DoesNotContain("pricing, demo", only.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Boilerplate_pages_contribute_nothing_however_well_they_read()
    {
        // The live example: Mozilla licence text on dext.com/licenses is grammatical, complete, and
        // catastrophic in a quote box on a page selling the partner.
        var pages = new[]
        {
            Page("https://dext.com/licenses",
                "Larger Work means a work that combines Covered Software with other material."),
            Page("https://dext.com/data-processor-agreement",
                "We process personal data only on documented instructions from the controller."),
            Page("https://www.avidxchange.com/california-notice-at-collection/",
                "We may collect personal information about you when you visit our website today."),
        };

        Assert.Empty(GccQuoteCandidates.From(pages));
    }

    [Fact]
    public void A_fragment_is_not_a_candidate()
    {
        // Mid-sentence text, lowercase start, no terminator — the shape a naive scan returns.
        var pages = new[] { Page(Url, "removed 90% of the manual elements from the process") };

        Assert.Empty(GccQuoteCandidates.From(pages));
    }

    [Fact]
    public void A_heading_or_label_is_not_a_candidate()
    {
        var pages = new[] { Page(Url, "Do Not Sell or Share My Personal Information Today Please") };

        Assert.Empty(GccQuoteCandidates.From(pages));
    }

    [Fact]
    public void Table_rows_and_bullet_runs_are_not_candidates()
    {
        var pages = new[]
        {
            Page(Url, "Starter | Growth | Enterprise | Unlimited invoices and approvals included."),
            Page("https://stampli.com/features/", "• Approvals • Payments • Capture and match everything."),
        };

        Assert.Empty(GccQuoteCandidates.From(pages));
    }

    [Fact]
    public void Several_sentences_in_one_line_each_become_a_candidate()
    {
        var pages = new[]
        {
            Page(Url,
                "We chased paper around the building for days before this arrived. Stampli plays an "
                + "important part of our daily AP process now. Invoices route themselves without "
                + "anyone chasing them at all."),
        };

        var candidates = GccQuoteCandidates.From(pages);

        Assert.Equal(3, candidates.Count);
        Assert.Equal([1, 2, 3], candidates.Select(c => c.Id));
    }

    [Fact]
    public void An_abbreviation_does_not_split_a_sentence()
    {
        var pages = new[]
        {
            Page(Url, "Our team at Acme Inc. cut approval time from nine days to two this quarter."),
        };

        var only = Assert.Single(GccQuoteCandidates.From(pages));
        Assert.Contains("Inc. cut", only.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_sentence_on_two_pages_is_offered_once()
    {
        // Boilerplate marketing copy repeats across a site. Offering it twice wastes a candidate
        // slot and invites the selector to pick the duplicate.
        var pages = new[]
        {
            Page(Url, "Stampli plays an important part of our daily AP process every week."),
            Page("https://stampli.com/why/", "Stampli plays an important part of our daily AP process every week."),
        };

        Assert.Single(GccQuoteCandidates.From(pages));
    }

    [Fact]
    public void Candidate_ids_are_contiguous_from_one()
    {
        // The selector answers with a number and the text is read back by index. A gap would return
        // the wrong sentence, cited to the wrong page.
        var pages = Enumerable.Range(1, 6)
            .Select(i => Page($"https://stampli.com/case-studies/{i}/",
                $"Customer number {i} cut approval time from nine days to two this quarter."))
            .ToArray();

        var candidates = GccQuoteCandidates.From(pages);

        Assert.Equal(
            Enumerable.Range(1, candidates.Count),
            candidates.Select(c => c.Id));
    }

    [Fact]
    public void A_page_with_no_url_contributes_nothing()
    {
        // The URL is the cite. A candidate without one could be chosen and then have nothing to
        // attribute it to.
        Assert.Empty(GccQuoteCandidates.From([Page("", "Stampli plays an important part of our daily AP process.")]));
    }

    [Fact]
    public void The_candidate_list_is_capped()
    {
        var pages = Enumerable.Range(1, 60)
            .Select(i => Page($"https://stampli.com/case-studies/{i}/",
                $"Customer number {i} cut approval time from nine days down to two this quarter."))
            .ToArray();

        Assert.True(GccQuoteCandidates.From(pages).Count <= GccQuoteCandidates.MaxCandidates);
    }
}
