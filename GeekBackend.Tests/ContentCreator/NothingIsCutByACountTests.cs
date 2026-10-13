using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.ContentCreator.Partner;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Nothing a search brings back is left out for a count (Jeff, 2026-10-12: "STOP NOT USING DATA, EVEN
/// IF THAT MEANS IT COSTS MORE"). Each test here names a count that used to cut research between a
/// search and a writer, and fails if it comes back. What a request carries is limited by what its
/// model accepts and by nothing else; that is in <see cref="GccRequestThatFitsTests"/>.
/// </summary>
/// <remarks>
/// The pages written through the real writers, with research past every one of these counts at once,
/// are in <c>BriefFieldReachTests.Nothing_a_search_brought_back_is_left_out_of_a_request_for_a_count</c>.
/// </remarks>
public class NothingIsCutByACountTests
{
    // ---- where a search's passages become a page ----------------------------------------------------

    private static HttpGeekCrawlerRagClient.ChunkDto Chunk(string url, int index, string text, string? parent = null) => new()
    {
        Url = url,
        FinalUrl = url,
        Title = "A page",
        ChunkIndex = index,
        Text = text,
        ChunkRole = parent is null ? null : "child",
        ParentText = parent,
        Score = 0.9 - (index * 0.001),
    };

    /// <summary>A page kept eighty passages at most, and stopped at an estimated 16,000 tokens.</summary>
    [Fact]
    public void A_page_keeps_every_passage_a_search_returned_for_it_however_many_and_however_long()
    {
        // 120 passages of about 4,000 characters: 480,000 characters on one page, where the page was
        // cut at eighty passages and again at about 64,000 characters.
        var filler = string.Join(' ', Enumerable.Range(0, 480).Select(n => $"item{(n * 7919) % 10007}"));
        var chunks = Enumerable.Range(0, 120)
            .Select(i => Chunk("https://partner.test/long", i, $"Passage {i:000}. {filler}"))
            .ToList();

        var page = Assert.Single(HttpGeekCrawlerRagClient.MapChunksToQuoteable(chunks));

        Assert.Equal(120, page.Paragraphs.Count);
        Assert.Equal(120, page.Scores!.Count);
        // In the order they are read on the page.
        Assert.All(Enumerable.Range(0, 120), i => Assert.StartsWith($"Passage {i:000}. ", page.Paragraphs[i], StringComparison.Ordinal));
    }

    /// <summary>A passage and the section around it were each cut at 2,000 characters.</summary>
    [Fact]
    public void A_passage_and_the_section_around_it_are_printed_whole()
    {
        var detail = string.Concat(Enumerable.Repeat("The approval chain is set once. ", 90)) + "THE LAST WORDS OF THE PASSAGE.";
        var section = string.Concat(Enumerable.Repeat("Each invoice is routed by amount. ", 90)) + "THE LAST WORDS OF THE SECTION.";
        Assert.InRange(detail.Length, 2_001, int.MaxValue);
        Assert.InRange(section.Length, 2_001, int.MaxValue);

        var page = Assert.Single(HttpGeekCrawlerRagClient.MapChunksToQuoteable(
            [Chunk("https://partner.test/a", 0, detail, parent: section)]));

        var passage = Assert.Single(page.Paragraphs);
        Assert.Contains($"Specific detail: {detail}", passage, StringComparison.Ordinal);
        Assert.Contains($"Context: {section}", passage, StringComparison.Ordinal);
    }

    // ---- the page's research: what the brief's searches and the question searches found -------------

    private static readonly GccPassageSearch Problem = new(GccPassageSearch.KindProblem, "Approvals stall in email.");

    private static GccQuoteablePage Found(string url, GccPassageSearch by, params string[] passages) =>
        GccGroundingResolver.FoundBy(
            new GccQuoteablePage(url, "A page", [], passages, RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk), by);

    /// <summary>
    /// What a question's search found reached the one request that answers the question. It is part of
    /// the page's research: joined to the pages the brief's searches found, each passage once.
    /// </summary>
    [Fact]
    public void What_the_question_searches_found_joins_the_pages_research_each_passage_once()
    {
        var research = new GccResearchDocument(
            null,
            [
                Found("https://partner.test/product", Problem, "It routes each invoice.", "It syncs to Xero."),
                Found("https://partner.test/pricing", Problem, "It is billed monthly."),
            ])
        {
            FaqEvidence =
            [
                // On a page the brief's search already found: one passage it has, one it has not.
                new GccFaqEvidence("partner.test", "Does it sync with Xero?",
                    [new GccQuoteablePage("https://PARTNER.test/product", "A page", [], ["It syncs to Xero.", "Xero bills post nightly."])]),
                // On a page only this question found.
                new GccFaqEvidence("partner.test", "Is there an audit trail?",
                    [new GccQuoteablePage("https://partner.test/security", "Security", [], ["Every approval is logged."])]),
                // A search that found nothing adds nothing.
                new GccFaqEvidence("partner.test", "Does it file sales tax?", []),
            ],
        };

        var pages = GccPageResearch.PagesOf(research);

        Assert.Equal(
            ["https://partner.test/product", "https://partner.test/pricing", "https://partner.test/security"],
            pages.Select(page => page.Url));
        Assert.Equal(["It routes each invoice.", "It syncs to Xero.", "Xero bills post nightly."], pages[0].Paragraphs);
        Assert.Equal(["Every approval is logged."], pages[2].Paragraphs);

        var xero = new GccPassageSearch(GccPassageSearch.KindQuestion, "Does it sync with Xero?");
        var audit = new GccPassageSearch(GccPassageSearch.KindQuestion, "Is there an audit trail?");
        Assert.Equal([Problem], pages[0].SearchesThatFound(0));
        // Found by the brief's search and by the question's: printed once, and it says both.
        Assert.Equal([Problem, xero], pages[0].SearchesThatFound(1));
        Assert.Equal([xero], pages[0].SearchesThatFound(2));
        Assert.Equal([audit], pages[2].SearchesThatFound(0));
        Assert.Equal([Problem, xero, audit], GccPageResearch.SearchesOf(pages));

        // The research as it is stored is not changed: the question's own request still reads its own search.
        Assert.Equal(2, research.Quoteables[0].Paragraphs.Count);
        // The same research gives the same list, so every request of a page carries the same text.
        Assert.Equal(
            GccPageResearch.PagesOf(research).SelectMany(page => page.Paragraphs),
            pages.SelectMany(page => page.Paragraphs));
    }

    [Fact]
    public void Research_with_no_question_search_is_the_pages_the_briefs_searches_found()
    {
        var research = new GccResearchDocument(null, [Found("https://partner.test/product", Problem, "It routes each invoice.")]);

        Assert.Same(research.Quoteables[0], Assert.Single(GccPageResearch.PagesOf(research)));
        Assert.Empty(GccPageResearch.PagesOf(null));
    }

    /// <summary>A tool page's create holds its own partner's question searches alone, and so does its research.</summary>
    [Fact]
    public void A_tool_pages_research_carries_its_own_partners_question_passages_and_no_other_partners()
    {
        var create = new GccCreateDto(
            Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
            StartingContentType: "tool", Topic: "Topic", Notes: null, ProjectSiteRunId: null, SiteSectionJson: null,
            BriefJson: null,
            ResearchJson: GccResearchFetchService.Serialize(new GccResearchDocument(
                null,
                [
                    Found("https://dext.com/product", Problem, "Dext reads the receipt."),
                    Found("https://bill.com/product", Problem, "BILL routes the approval."),
                ])
            {
                FaqEvidence =
                [
                    new GccFaqEvidence("dext.com", "Does Dext read handwriting?",
                        [new GccQuoteablePage("https://dext.com/help", "Help", [], ["Dext reads printed and handwritten receipts."])]),
                    new GccFaqEvidence("bill.com", "Does BILL pay by card?",
                        [new GccQuoteablePage("https://bill.com/pay", "Pay", [], ["BILL pays vendors by card."])]),
                ],
            }),
            Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow, ProjectId: Guid.NewGuid());
        var dext = Assert.Single(
            GccPartnerToolSlices.Build(create, ["https://dext.com", "https://bill.com"], []), slice => slice.Host == "dext.com");

        var block = GccGenerateService.BuildResearchBlock(dext.Narrow(create));

        Assert.Contains("- Dext reads the receipt.", block, StringComparison.Ordinal);
        Assert.Contains("- Dext reads printed and handwritten receipts.", block, StringComparison.Ordinal);
        Assert.Contains("The question: Does Dext read handwriting?", block, StringComparison.Ordinal);
        Assert.DoesNotContain("BILL", block, StringComparison.Ordinal);
    }

    // ---- a partner's help and documentation pages ---------------------------------------------------

    /// <summary>
    /// A page on a partner's help or documentation address belonged to no declared partner, so it
    /// reached no tool page. It is that partner's, by the rule the pillar and the blog count a
    /// partner's research by.
    /// </summary>
    [Fact]
    public void A_page_on_a_partners_help_address_is_in_that_partners_tool_page()
    {
        var create = new GccCreateDto(
            Id: Guid.NewGuid(), ClientId: Guid.NewGuid(), OwnerUserId: Guid.NewGuid(),
            StartingContentType: "tool", Topic: "Topic", Notes: null, ProjectSiteRunId: null, SiteSectionJson: null,
            BriefJson: null,
            ResearchJson: GccResearchFetchService.Serialize(new GccResearchDocument(
                null,
                [
                    Found("https://dext.com/product", Problem, "Dext reads the receipt."),
                    Found("https://help.dext.com/setup", Problem, "Setup takes one afternoon."),
                    Found("https://docs.bill.com/api", Problem, "The API pages through invoices."),
                ])),
            Status: "draft", CreatedAtUtc: DateTime.UtcNow, UpdatedAtUtc: DateTime.UtcNow, ProjectId: Guid.NewGuid());
        var typed = new[]
        {
            new GccGroundedPassage("https://help.dext.com/setup", "Help", [new TextParagraph([new Run("Setup takes one afternoon.")])]),
        };

        var slices = GccPartnerToolSlices.Build(create, ["https://dext.com", "https://www.bill.com/"], typed);

        var dext = Assert.Single(slices, slice => slice.Host == "dext.com");
        Assert.Equal(["https://dext.com/product", "https://help.dext.com/setup"], dext.Pages.Select(page => page.Url));
        Assert.Equal("https://help.dext.com/setup", Assert.Single(dext.Passages).Url);
        var bill = Assert.Single(slices, slice => slice.Host == "bill.com");
        Assert.Equal("https://docs.bill.com/api", Assert.Single(bill.Pages).Url);
    }

    // ---- the partner data ---------------------------------------------------------------------------

    /// <summary>
    /// The partner data was read from a page's first forty passages, each cut at 1,200 characters,
    /// and its first eight headings.
    /// </summary>
    [Fact]
    public void The_partner_data_is_read_from_every_passage_of_a_page_whole()
    {
        var longPassage = string.Concat(Enumerable.Repeat("The plan is billed per seat. ", 60)) + "ENTERPRISE IS PRICED ON REQUEST.";
        Assert.InRange(longPassage.Length, 1_201, int.MaxValue);
        var page = new GccQuoteablePage(
            "https://partner.test/pricing",
            "Pricing",
            [.. Enumerable.Range(1, 12).Select(n => new HeadingDto(2, $"Heading {n:00}"))],
            [longPassage, .. Enumerable.Range(2, 44).Select(n => $"Passage {n:00} of the pricing page.")]);

        var prompt = GccPartnerExtractionService.BuildUserPrompt(page, ["Partner Widget"]);

        Assert.Contains("ENTERPRISE IS PRICED ON REQUEST.", prompt, StringComparison.Ordinal);
        Assert.Contains("Passage 45 of the pricing page.", prompt, StringComparison.Ordinal);
        Assert.Contains("H2: Heading 12", prompt, StringComparison.Ordinal);
    }

    // ---- the quotable sentences ---------------------------------------------------------------------

    private sealed class CountingPages(IReadOnlyList<GeekCrawlerPageDto> crawled) : IGccCrawlPageReader
    {
        public List<int> Reads { get; } = [];

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId, IReadOnlyList<string> seedUrls, CancellationToken ct = default)
        {
            Reads.Add(seedUrls.Count);
            var asked = seedUrls.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([.. crawled.Where(page => asked.Contains(page.Url))]);
        }

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPageBlocksAsync(
            Guid runId, int limit = 100, int offset = 0, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GeekCrawlerPageDto>>([]);
    }

    /// <summary>
    /// The repository takes 32 addresses a read, and the pages past the first 32 were not read at all,
    /// so no sentence of theirs could be quoted. Every page is read, 32 at a time.
    /// </summary>
    [Fact]
    public async Task Every_page_is_read_for_quotable_sentences_thirty_two_at_a_time()
    {
        var urls = Enumerable.Range(1, 70).Select(n => $"https://partner.test/page-{n:00}").ToList();
        var crawled = urls
            .Select(url => GccCompetitorAnalysisResolverTests.BlockPage(
                url, GccCompetitorAnalysisResolverTests.Paragraph($"What {url} says of the product, in a sentence long enough to quote.")))
            .ToList();
        var pages = new CountingPages(crawled);
        var retrieved = urls.Select(url => new GccQuoteablePage(url, "A page", [], ["A passage."])).ToList();

        var typed = await new GccTypedPassageReader(pages).ReadAsync(Guid.NewGuid(), retrieved, CancellationToken.None);

        Assert.Equal([32, 32, 6], pages.Reads);
        Assert.Equal(urls, typed.Select(passage => passage.Url));
    }

    // ---- competitor pages and their outlines --------------------------------------------------------

    private static GccQuoteablePage Returned(string url) =>
        new(url, "A page", [], ["A passage."], RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk);

    private static GccCompetitorPageAnalysis Outline(string url, string heading) =>
        new(url, [new SiteStructureNode(2, heading, [], [], [])]);

    [Theory]
    [InlineData("https://rival.test/Pricing/", "rival.test/pricing")]
    [InlineData("http://www.rival.test/pricing", "rival.test/pricing")]
    [InlineData("https://RIVAL.test/pricing#plans", "rival.test/pricing")]
    [InlineData("https://rival.test/", "rival.test")]
    [InlineData("https://rival.test/search?q=AP", "rival.test/search?q=ap")]
    [InlineData("not an address", "")]
    [InlineData(null, "")]
    public void Two_readings_of_one_page_are_compared_by_one_address(string? url, string key) =>
        Assert.Equal(key, GccCompetitorOutlines.AddressKey(url));

    [Fact]
    public void A_returned_page_has_the_outline_whose_address_is_its_own_and_a_miss_is_named()
    {
        var returned = new[]
        {
            Returned("http://www.rival.test/Pricing/"),
            Returned("https://rival.test/blog/post"),
            Returned("https://other.test/"),
        };
        var analyses = new[]
        {
            Outline("https://rival.test/about", "About"),
            Outline("https://rival.test/pricing", "Plans"),
            Outline("https://other.test", "Home"),
        };

        var outlines = GccCompetitorOutlines.For(returned, analyses);

        Assert.Same(analyses[1], outlines[0]);
        Assert.Null(outlines[1]);
        Assert.Same(analyses[2], outlines[2]);
        Assert.Equal(["https://rival.test/blog/post"], GccCompetitorOutlines.WithoutOutline(returned, analyses));
        Assert.Empty(GccCompetitorOutlines.For(null, analyses));
    }

    /// <summary>
    /// When a request cannot show every competitor page, the ones kept are each competitor's first in
    /// turn. The first five by position were five pages of the first competitor and none of the others.
    /// </summary>
    [Fact]
    public void The_competitor_pages_kept_when_a_request_is_over_are_one_from_each_competitor_in_turn()
    {
        // Three competitors, eight pages each, in the order the searches returned them.
        var returned = new[] { "a.test", "b.test", "c.test" }
            .SelectMany(host => Enumerable.Range(1, 8).Select(n => Returned($"https://{host}/page-{n}")))
            .ToList();

        Assert.Equal(Enumerable.Range(0, 24), GccCompetitorOutlines.Shown(returned, all: true).OrderBy(i => i));

        var kept = GccCompetitorOutlines.Shown(returned, all: false);
        Assert.Equal(GccCompetitorOutlines.KeptWhenOver, kept.Count);
        Assert.Equal(
            ["https://a.test/page-1", "https://a.test/page-2", "https://b.test/page-1", "https://b.test/page-2", "https://c.test/page-1"],
            kept.OrderBy(i => i).Select(i => returned[i].Url));

        var gaveWay = GccCompetitorOutlines.GivenWay(returned);
        Assert.Equal(19, gaveWay.Count);
        Assert.DoesNotContain("https://c.test/page-1", gaveWay);
        Assert.Contains("https://c.test/page-2", gaveWay);

        // A project with no more competitor pages than are kept has none that give way.
        Assert.Empty(GccCompetitorOutlines.GivenWay([.. returned.Take(5)]));
    }

    // ---- what is read beside the searches -----------------------------------------------------------

    /// <summary>
    /// The publisher's home page stopped at its 24th heading and 40th block of text, and a block was cut
    /// at 600 characters.
    /// </summary>
    [Fact]
    public async Task The_publishers_home_page_is_read_whole()
    {
        var longBlock = "What we do for a finance team. " + string.Join(' ', Enumerable.Range(0, 300).Select(n => $"word{n}"));
        var blocks = new List<object>();
        for (var section = 0; section < 30; section++)
        {
            blocks.Add(new { type = "heading", level = 2, text = $"Section {section:00}" });
            blocks.Add(new { type = "paragraph", text = $"Paragraph {section:00} a." });
            blocks.Add(new { type = "listItem", text = $"Paragraph {section:00} b." });
        }
        blocks.Add(new { type = "paragraph", text = longBlock });

        var runId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var project = new GccProjectDto(
            projectId, Guid.NewGuid(), "Acme", null, null, "active", "https://publisher.test", runId, "accounting",
            [], [], DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
        var home = new GeekCrawlerPageDto(
            Guid.NewGuid(), runId, "https://publisher.test", "https://publisher.test", "https://publisher.test",
            200, true, null, null, DateTimeOffset.UtcNow,
            Blocks: JsonSerializer.SerializeToElement(blocks));

        var profile = await new GccPublisherProfileResolver(
            new GccCompetitorAnalysisResolverTests.FakeProjects(project),
            new GccCompetitorAnalysisResolverTests.FakePages([home]),
            NullLogger<GccPublisherProfileResolver>.Instance).ResolveAsync(projectId);

        Assert.Equal(Enumerable.Range(0, 30).Select(section => $"Section {section:00}"), profile.Headings);
        Assert.Equal(61, profile.Paragraphs.Count);
        Assert.Equal("Paragraph 29 b.", profile.Paragraphs[59]);
        Assert.Equal(longBlock, profile.Paragraphs[60]);
        Assert.InRange(longBlock.Length, 2_000, int.MaxValue);
    }

    /// <summary>The first 30 amounts not in US dollars were named for the writer and the rest were not.</summary>
    [Fact]
    public void Every_amount_that_is_not_in_dollars_is_named_for_the_writer()
    {
        var evidence = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 45).Select(n => $"The plan for a team of {n} is £{1000 + n:N0} a year."));

        var note = GccCurrencyGrammar.ForeignAmountsInstruction(evidence);

        Assert.NotNull(note);
        Assert.All(
            Enumerable.Range(1, 45),
            n => Assert.Contains($"- £{1000 + n:N0} (GBP)" + Environment.NewLine, note!, StringComparison.Ordinal));
    }

    /// <summary>The request that reads a partner's data from a page was told the first twelve product names.</summary>
    [Fact]
    public void The_partner_data_request_names_every_partner_product_of_the_project()
    {
        var names = Enumerable.Range(1, 20).Select(n => $"Product{n:00}").ToList();
        var page = new GccQuoteablePage(
            "https://partner.test/pricing", "Pricing", [], ["What it costs."],
            RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk);

        var prompt = GccPartnerExtractionService.BuildUserPrompt(page, names);

        Assert.Contains("Partner product names in scope: " + string.Join(", ", names) + Environment.NewLine, prompt, StringComparison.Ordinal);
    }
}
