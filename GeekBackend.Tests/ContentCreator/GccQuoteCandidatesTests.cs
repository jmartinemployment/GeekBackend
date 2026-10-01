using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// What a block quotation may be chosen from.
///
/// <para>
/// Candidates are cut here so the model never retypes a sentence — it answers with a number, and
/// the text is read back from this list. Everything these tests pin is therefore load-bearing: a
/// bad candidate cannot be caught later, because there is no later check.
/// </para>
/// <para>
/// The input is a partner page's typed blocks, which is the only source there is. It used to also
/// accept a retrieved page, whose text is the prompt projection with <c>Section:</c> /
/// <c>Specific detail:</c> labels interleaved, and a test here pinned the stripping of those labels.
/// Both are gone: the labelled blob is no longer an input to anything.
/// </para>
/// </summary>
public class GccQuoteCandidatesTests
{
    private const string Url = "https://stampli.com/case-studies/cti/";

    private static GccGroundedPassage Passage(string url, params string[] paragraphs) =>
        new(url, "CTI case study", paragraphs.Select(Prose).ToList());

    private static Paragraph Prose(string text) => new TextParagraph([new Run(text)]);

    [Fact]
    public void A_complete_sentence_becomes_a_candidate_carrying_its_page()
    {
        var passages = new[] { Passage(Url, "Stampli plays an important part of our daily AP process every week.") };

        var candidates = GccQuoteCandidates.From(passages);

        var only = Assert.Single(candidates);
        Assert.Equal(1, only.Id);
        Assert.Equal(Url, only.PageUrl);
        Assert.StartsWith("Stampli plays", only.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_block_the_page_marked_as_a_quotation_is_offered_whole()
    {
        // Two sentences inside one quote block. Splitting them would cut a quotation in half and
        // attribute the halves separately -- the page already said where this one starts and ends.
        var quoted = "We chased paper for days. We have cut approval time from nine days to two.";
        var passages = new[]
        {
            new GccGroundedPassage(Url, "CTI case study",
                [new QuoteParagraph([new Run(quoted)], Url)]),
        };

        var only = Assert.Single(GccQuoteCandidates.From(passages));
        Assert.Equal(quoted, only.Text);
    }

    [Fact]
    public void A_declared_quotation_is_held_to_length_and_not_to_sentence_shape()
    {
        // No terminator and a lowercase start -- a shape test rejects it. The crawler typed it as a
        // quotation because the page marked it as one, which outranks guessing from punctuation.
        var pulled = "approvals went from nine days to two, and nobody has looked back since then";
        var passages = new[]
        {
            new GccGroundedPassage(Url, "CTI case study",
                [new QuoteParagraph([new Run(pulled)], Url)]),
        };

        var only = Assert.Single(GccQuoteCandidates.From(passages));
        Assert.Equal(pulled, only.Text);
    }

    [Fact]
    public void A_declared_quotation_is_still_held_to_length()
    {
        // Length is the one bound a typed quotation keeps: three words carries no point and a page
        // of prose is not a quotation however the page marked it.
        var passages = new[]
        {
            new GccGroundedPassage(Url, "CTI case study",
                [new QuoteParagraph([new Run("It works well.")], Url)]),
        };

        Assert.Empty(GccQuoteCandidates.From(passages));
    }

    [Fact]
    public void Only_prose_and_quotations_are_offered()
    {
        // A list item is a fragment, a table row is cells joined for reading, a code sample is not
        // speech and a definition is the page's own glossary. None of them is something a partner
        // said, and each reads as prose right up to the moment it is in a quote box.
        var passages = new[]
        {
            new GccGroundedPassage(Url, "CTI case study",
            [
                new ListParagraph(false,
                [
                    [new Run("Approvals that route themselves without anyone chasing them at all.")],
                ]),
                new CodeParagraph("curl https://stampli.com/api/v1/invoices --header 'Accept: json'"),
                new DefinitionParagraph(
                [
                    new DefinitionItem(
                        [new Run("Accounts payable")],
                        [new Run("The money a business owes its suppliers for goods already delivered.")]),
                ]),
            ]),
        };

        Assert.Empty(GccQuoteCandidates.From(passages));
    }

    [Fact]
    public void Boilerplate_pages_contribute_nothing_however_well_they_read()
    {
        // The live example: Mozilla licence text on dext.com/licenses is grammatical, complete, and
        // catastrophic in a quote box on a page selling the partner.
        var passages = new[]
        {
            Passage("https://dext.com/licenses",
                "Larger Work means a work that combines Covered Software with other material."),
            Passage("https://dext.com/data-processor-agreement",
                "We process personal data only on documented instructions from the controller."),
            Passage("https://www.avidxchange.com/california-notice-at-collection/",
                "We may collect personal information about you when you visit our website today."),
        };

        Assert.Empty(GccQuoteCandidates.From(passages));
    }

    [Fact]
    public void A_fragment_is_not_a_candidate()
    {
        // Mid-sentence text, lowercase start, no terminator — the shape a naive scan returns.
        var passages = new[] { Passage(Url, "removed 90% of the manual elements from the process") };

        Assert.Empty(GccQuoteCandidates.From(passages));
    }

    [Fact]
    public void A_heading_or_label_is_not_a_candidate()
    {
        var passages = new[] { Passage(Url, "Do Not Sell or Share My Personal Information Today Please") };

        Assert.Empty(GccQuoteCandidates.From(passages));
    }

    [Fact]
    public void Table_rows_and_bullet_runs_are_not_candidates()
    {
        // The mapper joins a table row's cells on " | " and a bullet run can reach a paragraph block
        // on a page that marked it up as one, so the span test still refuses both.
        var passages = new[]
        {
            Passage(Url, "Starter | Growth | Enterprise | Unlimited invoices and approvals included."),
            Passage("https://stampli.com/features/", "• Approvals • Payments • Capture and match everything."),
        };

        Assert.Empty(GccQuoteCandidates.From(passages));
    }

    [Fact]
    public void Several_sentences_in_one_paragraph_each_become_a_candidate()
    {
        var passages = new[]
        {
            Passage(Url,
                "We chased paper around the building for days before this arrived. Stampli plays an "
                + "important part of our daily AP process now. Invoices route themselves without "
                + "anyone chasing them at all."),
        };

        var candidates = GccQuoteCandidates.From(passages);

        Assert.Equal(3, candidates.Count);
        Assert.Equal([1, 2, 3], candidates.Select(c => c.Id));
    }

    [Fact]
    public void An_abbreviation_does_not_split_a_sentence()
    {
        var passages = new[]
        {
            Passage(Url, "Our team at Acme Inc. cut approval time from nine days to two this quarter."),
        };

        var only = Assert.Single(GccQuoteCandidates.From(passages));
        Assert.Contains("Inc. cut", only.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_sentence_on_two_pages_is_offered_once()
    {
        // Boilerplate marketing copy repeats across a site. Offering it twice wastes a candidate
        // slot and invites the selector to pick the duplicate.
        var passages = new[]
        {
            Passage(Url, "Stampli plays an important part of our daily AP process every week."),
            Passage("https://stampli.com/why/", "Stampli plays an important part of our daily AP process every week."),
        };

        Assert.Single(GccQuoteCandidates.From(passages));
    }

    [Fact]
    public void Candidate_ids_are_contiguous_from_one()
    {
        // The selector answers with a number and the text is read back by index. A gap would return
        // the wrong sentence, cited to the wrong page.
        var passages = Enumerable.Range(1, 6)
            .Select(i => Passage($"https://stampli.com/case-studies/{i}/",
                $"Customer number {i} cut approval time from nine days to two this quarter."))
            .ToArray();

        var candidates = GccQuoteCandidates.From(passages);

        Assert.Equal(
            Enumerable.Range(1, candidates.Count),
            candidates.Select(c => c.Id));
    }

    [Fact]
    public void A_passage_with_no_url_contributes_nothing()
    {
        // The URL is the cite. A candidate without one could be chosen and then have nothing to
        // attribute it to.
        Assert.Empty(GccQuoteCandidates.From(
            [Passage("", "Stampli plays an important part of our daily AP process.")]));
    }

    [Fact]
    public void The_candidate_list_is_capped()
    {
        var passages = Enumerable.Range(1, 60)
            .Select(i => Passage($"https://stampli.com/case-studies/{i}/",
                $"Customer number {i} cut approval time from nine days down to two this quarter."))
            .ToArray();

        Assert.True(GccQuoteCandidates.From(passages).Count <= GccQuoteCandidates.MaxCandidates);
    }
}
