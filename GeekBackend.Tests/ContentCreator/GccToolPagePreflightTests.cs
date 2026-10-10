using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Partner;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The tool pre-flight: every declared partner is assessed before any partner is drafted, so three
/// pages out of five is something the operator is told rather than something they count.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these pin, and why it is not a page count.</b> The gate is extraction output — a capability
/// signal plus breadth across at least 3 of 20 categories — so a pre-flight built on retrieval volume
/// would pass a partner the gate then refuses. <see cref="A_partner_with_pages_but_no_extractable_payload_is_not_ready"/>
/// is that case directly: pages present, extraction empty, not ready.
/// </para>
/// <para>
/// <b>Mutation-checked.</b> Removing the pre-flight (drafting every slice and letting the gate throw)
/// leaves the outcomes identical — same count, same products, same refusals. Only the call counts and
/// the ordering assertion fail, which is why those are here rather than a tidier assertion on outcomes.
/// </para>
/// </remarks>
public class GccToolPagePreflightTests
{
    /// <summary>Features + integrations + FAQs: a capability signal and 3 of 20 categories, the gate's
    /// exact bar. One item less in any of the three and this partner is not ready.</summary>
    private static readonly PartnerPageExtraction GroundablePage = new(
        Citables: null,
        Advertisements: null,
        Comparisons: null,
        Alternatives: null,
        Pricing: null,
        Icp: null,
        Integrations: [new PartnerIntegrationItem("Xero", "accounting", null, null)],
        Faqs: [new PartnerFaqItem("Does it capture line items?", "Yes, on every invoice.", null)],
        CaseStudies: null,
        Testimonials: null,
        Awards: null,
        FeatureInventory: [new PartnerFeatureItem("Invoice data capture", "automation", null, null)],
        TechnicalConstraints: null,
        OfferCtas: null,
        Disqualifiers: null,
        UseCasePlaybooks: null,
        Categories: null,
        BattlecardSlices: null,
        DemoBeats: null,
        ComplianceSnippets: null);

    [Fact]
    public async Task Readiness_is_reported_for_every_declared_partner_before_anything_is_drafted()
    {
        // The ordering is the feature. A report that arrives after drafting is a post-mortem.
        // draftable, so a report that arrived after drafting would show a non-zero count. Without it
        // drafting never reaches the provider and the ordering assertion below holds either way.
        var fixtures = GccToolPageFanOutFixture.Build(
            GroundablePage,
            ["https://dext.com", "https://bill.com", "https://melio.com"],
            pagesPerPartner: 2,
            draftable: true);

        var draftsWhenReported = -1;
        IReadOnlyList<GccGenerateService.GccPartnerToolReadiness> reported = [];

        await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None,
            onReadiness: verdicts =>
            {
                reported = verdicts;
                draftsWhenReported = fixtures.Calls.Drafts;
                return Task.CompletedTask;
            });

        Assert.Equal(3, reported.Count);
        Assert.Equal(["Dext", "Bill", "Melio"], reported.Select(r => r.ProductName));
        Assert.Equal(0, draftsWhenReported);
        // ...and drafting did happen afterwards, which is what makes the zero above an ordering fact
        // rather than a fixture that never drafts at all.
        Assert.True(fixtures.Calls.Drafts > 0);
    }

    [Fact]
    public async Task A_partner_that_cannot_be_assessed_is_one_not_ready_finding_and_the_others_are_still_assessed_and_drafted()
    {
        // The pre-flight promises to report on every partner without any of them aborting the others.
        // The bank throwing for one of three used to abort all three.
        var fixtures = GccToolPageFanOutFixture.Build(
            GroundablePage,
            ["https://dext.com", "https://bill.com", "https://melio.com"],
            pagesPerPartner: 2,
            draftable: true,
            bank: new GccToolPageFanOutFixture.FakeExtractionBank { ThrowForHost = "bill.com" });

        var written = new List<GccGenerateJobEventWrite>();
        GccRunLog.Begin(Guid.NewGuid(), (events, _) => { written.AddRange(events); return Task.CompletedTask; }, NullLogger.Instance);

        IReadOnlyList<GccGenerateService.GccPartnerToolReadiness> reported = [];
        var outcomes = await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None,
            onReadiness: verdicts => { reported = verdicts; return Task.CompletedTask; });

        Assert.Equal(["Dext", "Bill", "Melio"], reported.Select(r => r.ProductName));
        var bill = Assert.Single(reported, r => r.ProductName == "Bill");
        Assert.False(bill.Ready);
        Assert.Contains("could not be assessed", bill.Coverage, StringComparison.Ordinal);
        Assert.Contains("the repository could not be reached for bill.com", bill.Coverage, StringComparison.Ordinal);
        Assert.True(reported.Where(r => r.ProductName != "Bill").All(r => r.Ready));

        // Every partner has an outcome, and Bill's is a refusal that names why.
        Assert.Equal(["Dext", "Bill", "Melio"], outcomes.Select(o => o.ProductName));
        Assert.Contains("could not be assessed", outcomes.Single(o => o.ProductName == "Bill").Refusal, StringComparison.Ordinal);
        Assert.True(fixtures.Calls.Drafts > 0);

        // The cause is in the run's record, with its type and stack, not only in a message.
        // (The scripted provider also fails Dext's and Melio's drafts; those are faults in the record too.)
        var faults = written.Where(w => w.Kind == "fault").Select(w => System.Text.Json.JsonDocument.Parse(w.PayloadJson)).ToList();
        var preflight = Assert.Single(faults, f => f.RootElement.GetProperty("step").GetString() == "tool pre-flight");
        var doc = preflight;
        Assert.Equal("Bill", doc.RootElement.GetProperty("partner").GetString());
        Assert.Equal("System.Net.Http.HttpRequestException", doc.RootElement.GetProperty("fault").GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_partner_with_pages_but_no_extractable_payload_is_not_ready()
    {
        // Volume is not the predictor: four retrieved pages, extraction finds nothing the schema covers.
        var fixtures = GccToolPageFanOutFixture.Build(
            GccPartnerExtractionFakes.EmptyPageExtraction,
            ["https://melio.com"],
            pagesPerPartner: 4);

        var verdict = await fixtures.Service.AssessPartnerToolReadinessAsync(
            new GccPartnerToolSlice(
                "melio.com",
                "Melio",
                GccResearchFetchService.Deserialize(fixtures.Create.ResearchJson)!.Quoteables,
                []), ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.False(verdict.Ready);
        Assert.Equal(0, verdict.PopulatedCategories);
        Assert.False(verdict.HasCapabilitySignal);
        Assert.Equal(4, verdict.PagesAttempted);
        Assert.Equal(0, verdict.PagesFailed);
        // The fault/shortage split, which is the whole value of the message: these pages extracted
        // cleanly and still yielded nothing, so this is a thin partner and not a provider outage.
        Assert.Contains("extracted cleanly", verdict.Coverage, StringComparison.Ordinal);
        Assert.Contains("0 of 20 payload categories", verdict.Coverage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_partner_meeting_the_gate_is_ready_and_carries_its_extraction_forward()
    {
        var fixtures = GccToolPageFanOutFixture.Build(
            GroundablePage, ["https://dext.com"], pagesPerPartner: 2);

        var verdict = await fixtures.Service.AssessPartnerToolReadinessAsync(
            new GccPartnerToolSlice(
                "dext.com",
                "Dext",
                GccResearchFetchService.Deserialize(fixtures.Create.ResearchJson)!.Quoteables,
                []), ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.True(verdict.Ready);
        Assert.True(verdict.HasCapabilitySignal);
        Assert.Equal(3, verdict.PopulatedCategories);
        // Carried, not re-derived: this is what stops a ready partner being extracted twice.
        Assert.NotNull(verdict.Extraction);
    }

    private static GccPartnerToolSlice SliceOf(GccToolPageFanOutFixture fixtures, string host, string product) =>
        new(
            host,
            product,
            [.. GccResearchFetchService.Deserialize(fixtures.Create.ResearchJson)!.Quoteables
                .Where(p => new Uri(p.Url).Host == host)],
            []);

    [Fact]
    public async Task The_same_pages_assessed_again_reuse_the_bank_and_make_no_extraction_call()
    {
        // The cost claim behind the bank: ~108 model calls per five partners were paid for on every
        // generate, four or five times on 2026-10-03, twice by runs that then died on a provider error.
        var fixtures = GccToolPageFanOutFixture.Build(GroundablePage, ["https://dext.com"], pagesPerPartner: 2);

        var first = await fixtures.Service.AssessPartnerToolReadinessAsync(SliceOf(fixtures, "dext.com", "Dext"), ContentGeneratorProvider.OpenAi, CancellationToken.None);
        var paid = fixtures.Calls.Extractions;
        Assert.True(paid > 0);
        Assert.False(first.Reused);
        Assert.Equal(1, fixtures.Bank.Count);

        var second = await fixtures.Service.AssessPartnerToolReadinessAsync(SliceOf(fixtures, "dext.com", "Dext"), ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.Equal(paid, fixtures.Calls.Extractions);
        Assert.True(second.Reused);
        Assert.NotNull(second.BankedAtUtc);
        Assert.True(second.Ready);
        Assert.NotNull(second.Extraction);
        // Said, never silent: the readiness line names the reuse.
        Assert.StartsWith("reused banked extraction from", second.Coverage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_changed_paragraph_on_one_page_re_extracts()
    {
        // The stamp is the content, not the run id or the URL set: a re-crawl refills the same run
        // id in place and yields the same URLs with new text, and both must miss the bank.
        var fixtures = GccToolPageFanOutFixture.Build(GroundablePage, ["https://dext.com"], pagesPerPartner: 2);
        var slice = SliceOf(fixtures, "dext.com", "Dext");
        await fixtures.Service.AssessPartnerToolReadinessAsync(slice, ContentGeneratorProvider.OpenAi, CancellationToken.None);
        var paid = fixtures.Calls.Extractions;

        var edited = slice with
        {
            Pages = [.. slice.Pages.Select((p, i) => i == 0
                ? p with { Paragraphs = ["The product now also matches purchase orders to invoices."] }
                : p)],
        };
        var verdict = await fixtures.Service.AssessPartnerToolReadinessAsync(edited, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.True(fixtures.Calls.Extractions > paid);
        Assert.False(verdict.Reused);
        Assert.Equal(2, fixtures.Bank.Count);
    }

    [Fact]
    public async Task A_second_run_on_overlapping_partners_pays_only_for_the_new_one()
    {
        // Jeff, 2026-10-04: "A new run may [have] different partners/competitors but there is
        // overlap." Keyed by host and digest rather than by create, the overlap is free.
        var firstRun = GccToolPageFanOutFixture.Build(
            GroundablePage, ["https://dext.com", "https://bill.com"], pagesPerPartner: 2);
        await firstRun.Service.AssessPartnerToolReadinessAsync(SliceOf(firstRun, "dext.com", "Dext"), ContentGeneratorProvider.OpenAi, CancellationToken.None);
        await firstRun.Service.AssessPartnerToolReadinessAsync(SliceOf(firstRun, "bill.com", "Bill"), ContentGeneratorProvider.OpenAi, CancellationToken.None);
        Assert.Equal(2, firstRun.Bank.Count);

        var secondRun = GccToolPageFanOutFixture.Build(
            GroundablePage, ["https://bill.com", "https://melio.com"], pagesPerPartner: 2, bank: firstRun.Bank);
        var bill = await secondRun.Service.AssessPartnerToolReadinessAsync(SliceOf(secondRun, "bill.com", "Bill"), ContentGeneratorProvider.OpenAi, CancellationToken.None);
        var paidBeforeMelio = secondRun.Calls.Extractions;
        var melio = await secondRun.Service.AssessPartnerToolReadinessAsync(SliceOf(secondRun, "melio.com", "Melio"), ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.True(bill.Reused);
        Assert.Equal(0, paidBeforeMelio);
        Assert.False(melio.Reused);
        Assert.True(secondRun.Calls.Extractions > 0);
        Assert.Equal(3, firstRun.Bank.Count);
    }

    [Fact]
    public async Task A_partner_whose_pages_failed_extraction_is_not_banked()
    {
        // Successes only. The failures of 2026-10-03 were a draining balance; freezing them in would
        // make a billing incident a permanent property of the partner.
        var fixtures = GccToolPageFanOutFixture.Build(
            GroundablePage, ["https://dext.com"], pagesPerPartner: 2, extractionFails: true);

        var verdict = await fixtures.Service.AssessPartnerToolReadinessAsync(SliceOf(fixtures, "dext.com", "Dext"), ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.False(verdict.Ready);
        Assert.Equal(2, verdict.PagesFailed);
        Assert.False(verdict.Reused);
        Assert.Equal(0, fixtures.Bank.Count);

        // And it is attempted again next time, not remembered as empty.
        var paid = fixtures.Calls.Extractions;
        await fixtures.Service.AssessPartnerToolReadinessAsync(SliceOf(fixtures, "dext.com", "Dext"), ContentGeneratorProvider.OpenAi, CancellationToken.None);
        Assert.True(fixtures.Calls.Extractions > paid);
    }

    [Fact]
    public void The_digest_is_the_pages_content_and_the_product_not_their_order()
    {
        var a = new GccQuoteablePage("https://dext.com/a", "A", [], ["Alpha."]);
        var b = new GccQuoteablePage("https://dext.com/b", "B", [], ["Beta."]);

        Assert.Equal(
            GccGenerateService.PartnerPagesDigest("Dext", [a, b]),
            GccGenerateService.PartnerPagesDigest("Dext", [b, a]));
        Assert.NotEqual(
            GccGenerateService.PartnerPagesDigest("Dext", [a, b]),
            GccGenerateService.PartnerPagesDigest("Dext", [a, b with { Paragraphs = ["Beta, revised."] }]));
        // Extraction is asked for one product by name, so a renamed partner is a different question.
        Assert.NotEqual(
            GccGenerateService.PartnerPagesDigest("Dext", [a, b]),
            GccGenerateService.PartnerPagesDigest("Dext Prepare", [a, b]));
    }

    [Fact]
    public async Task A_partner_with_no_retrieved_pages_reports_rather_than_disappearing()
    {
        var fixtures = GccToolPageFanOutFixture.Build(GroundablePage, ["https://dext.com"], pagesPerPartner: 0);

        var verdict = await fixtures.Service.AssessPartnerToolReadinessAsync(
            new GccPartnerToolSlice("dext.com", "Dext", [], []), ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.False(verdict.Ready);
        Assert.Equal("no extractable partner pages", verdict.Coverage);
        Assert.Null(verdict.Extraction);
        // Nothing to extract from means nothing was extracted -- not an empty extraction call.
        Assert.Equal(0, fixtures.Calls.Extractions);
    }

    [Fact]
    public async Task A_partner_the_preflight_refuses_is_never_drafted()
    {
        // The cost claim. Before this, every partner was drafted and the gate threw inside, so a project
        // with two thin partners paid for two writes that could not have been grounded.
        var fixtures = GccToolPageFanOutFixture.Build(
            GccPartnerExtractionFakes.EmptyPageExtraction,
            ["https://dext.com", "https://melio.com"],
            pagesPerPartner: 2,
            draftable: true);

        var outcomes = await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.False(o.Written));
        // Zero provider calls, with a brief that would have let drafting through. Without the brief this
        // assertion holds whether or not the pre-flight exists, which is no assertion at all.
        Assert.Equal(0, fixtures.Calls.Drafts);
        // Extraction ran once per page for the pre-flight and not again: 2 pages x 2 partners.
        Assert.Equal(4, fixtures.Calls.Extractions);
    }

    [Fact]
    public async Task The_refusal_names_the_product_the_host_and_the_coverage()
    {
        // "Refused:" is load-bearing -- GenerateAsync filters on that prefix to answer 400 rather than
        // 503, so a refusal without it is reported to the operator as though GeekAPI were down.
        var fixtures = GccToolPageFanOutFixture.Build(
            GccPartnerExtractionFakes.EmptyPageExtraction, ["https://melio.com"], pagesPerPartner: 3);

        var outcomes = await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        var only = Assert.Single(outcomes);
        Assert.StartsWith("Refused:", only.Refusal!, StringComparison.Ordinal);
        Assert.Contains("Melio", only.Refusal!, StringComparison.Ordinal);
        Assert.Contains("melio.com", only.Refusal!, StringComparison.Ordinal);
        Assert.Contains("payload categories", only.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_ready_partner_is_extracted_once_for_the_whole_generate()
    {
        // Two pages, one partner: extraction is one call per page, so a ready partner that was extracted
        // in the pre-flight and again to draft would show four.
        var fixtures = GccToolPageFanOutFixture.Build(
            GroundablePage, ["https://dext.com"], pagesPerPartner: 2, draftable: true);

        var outcomes = await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        // Two, not four. Drafting was genuinely entered -- the provider was called and refused -- so a
        // second extraction would have happened here if the pre-flight's document were not carried in.
        Assert.Equal(2, fixtures.Calls.Extractions);

        // And it was not the pre-flight that refused it: a pre-flight refusal names the coverage.
        var only = Assert.Single(outcomes);
        Assert.DoesNotContain("payload categories", only.Refusal!, StringComparison.Ordinal);
        // A failure here must say what stopped drafting, or the next reader re-derives it.
        Assert.True(fixtures.Calls.Drafts > 0, $"drafting never reached the provider: {only.Refusal}");
    }
}
