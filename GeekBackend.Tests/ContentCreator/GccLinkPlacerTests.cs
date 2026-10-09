using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The writer marks a run with a target id; the code puts the link there. Every way the writer used to
/// get a link wrong -- the Stampli page's ten paragraph-long links, the pillar's four, Ramp's URL from
/// memory, and the seven pages refused on 2026-10-09 for anchor words copied inexactly from their own
/// paragraph -- is either impossible in this shape or a refusal here by name, and nothing is repaired.
/// </summary>
public sealed class GccLinkPlacerTests
{
    private const string Scheduler = "#consultationAppointment2xl";
    private const string PartnerUrl = "https://www.stampli.com/ap-automation/";
    private const string ToolPath = "/tools/accounting/cash-flow-forecasting/chaserhq";

    private static readonly GccLinkTargets Targets = GccLinkTargets.For(
        [new GccQuoteablePage(PartnerUrl, "AP Automation", [], ["Text."])],
        [new KnownCrawlTool("Chaser", null, ToolPath)]);

    private static Section Body(string heading, params Paragraph[] paragraphs) =>
        new("h2", heading, paragraphs, null, []);

    private static ContentDocument Doc(params Section[] sections) =>
        new(Body("Opening", new TextParagraph([new Run("An opening with no links.")])), sections);

    private static TextParagraph Text(params Run[] runs) => new(runs);

    private static Run Linked(string text, string target) => new(text, Link: target);

    private static IReadOnlyList<Run> RunsOf(ContentDocument document, int section = 0, int paragraph = 0) =>
        ((TextParagraph)document.Sections[section].Paragraphs[paragraph]).Runs;

    // ---- placing ------------------------------------------------------------------------------------

    [Fact]
    public void TheLinkLandsOnTheMarkedRunAndNothingElse()
    {
        var doc = Doc(Body("How it works",
            Text(new Run("Stampli's "), Linked("AP automation", "S1"), new Run(" matches invoices to purchase orders."))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        var runs = RunsOf(placed.Document);
        Assert.Equal(["Stampli's ", "AP automation", " matches invoices to purchase orders."], runs.Select(r => r.Text));
        Assert.Equal([null, PartnerUrl, null], runs.Select(r => r.Href));
        // The words are the writer's, untouched: joined back they are the original sentence.
        Assert.Equal("Stampli's AP automation matches invoices to purchase orders.", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void ThePlacedRunCarriesTheHrefAndNoIdAnyMore()
    {
        var doc = Doc(Body("A", Text(Linked("Chaser", "T1"), new Run(" chases."))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        var run = RunsOf(placed.Document)[0];
        Assert.Equal(ToolPath, run.Href);
        Assert.Null(run.Link);
    }

    [Fact]
    public void TheWordsAreNeverSearchedForSoTheirCaseAndWordingCannotRefuse()
    {
        // Versapay, 2026-10-09: anchor "real-time dashboards", run "Real-time dashboards give...". Upflow:
        // anchor "Upflow syncs with several software tools", run "Upflow natively syncs with...". Both
        // refused as "not in the paragraph". The run is the anchor now; there is nothing to not find.
        var doc = Doc(Body("A",
            Text(Linked("Real-time dashboards", "S1"), new Run(" give management the insights they need.")),
            Text(Linked("Upflow natively syncs with several software tools", "T1"), new Run(", including Xero."))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        Assert.Equal(PartnerUrl, RunsOf(placed.Document, 0, 0)[0].Href);
        Assert.Equal(ToolPath, RunsOf(placed.Document, 0, 1)[0].Href);
    }

    [Fact]
    public void TwoLinksInOneParagraphAreBothPlaced()
    {
        var doc = Doc(Body("A",
            Text(Linked("Chaser", "T1"), new Run(" and "), Linked("AP automation", "S1"), new Run(" together."))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        var runs = RunsOf(placed.Document);
        Assert.Equal(["Chaser", " and ", "AP automation", " together."], runs.Select(r => r.Text));
        Assert.Equal([ToolPath, null, PartnerUrl, null], runs.Select(r => r.Href));
    }

    [Fact]
    public void AListItemTakesTheLinkOnItsMarkedRun()
    {
        var list = new ListParagraph(false,
            [[new Run("Invoices are matched.")], [Linked("Chaser", "T1"), new Run(" sends the reminders.")]]);
        var doc = Doc(Body("A", list));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        var items = ((ListParagraph)placed.Document.Sections[0].Paragraphs[0]).Items;
        Assert.Equal(["Invoices are matched."], items[0].Select(r => r.Text));
        Assert.Equal(["Chaser", " sends the reminders."], items[1].Select(r => r.Text));
        Assert.Equal(ToolPath, items[1][0].Href);
        Assert.Null(items[1][0].Link);
    }

    [Fact]
    public void ChildrenAndTheLedeArePlacedToo()
    {
        var child = new Section("h3", "Child", [Text(Linked("Chaser", "T1"), new Run(" here."))], null, []);
        var doc = new ContentDocument(
            new Section("h2", "Opening", [Text(Linked("AP automation", "S1"), new Run(" first."))], null, []),
            [new Section("h2", "Parent", [], null, [child])]);

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        Assert.Equal(PartnerUrl, ((TextParagraph)placed.Document.Lede.Paragraphs[0]).Runs[0].Href);
        Assert.Equal(ToolPath, ((TextParagraph)placed.Document.Sections[0].Children[0].Paragraphs[0]).Runs[0].Href);
    }

    [Fact]
    public void AParagraphWithNoLinksIsUnchanged()
    {
        var doc = Doc(Body("A", Text(new Run("Nothing to link here."))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        var run = Assert.Single(RunsOf(placed.Document));
        Assert.Equal("Nothing to link here.", run.Text);
        Assert.Null(run.Href);
    }

    // ---- refusing -----------------------------------------------------------------------------------

    [Fact]
    public void ATargetThePromptDidNotPrintIsRefusedAndTheTargetsAreNamed()
    {
        // Ramp, 2026-10-09: a URL from memory. There is no field for one now, and an id that was not
        // printed is the same thing said another way.
        var doc = Doc(Body("A", Text(Linked("Ramp", "S9"), new Run(" saves money."))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        var refusal = Assert.Single(placed.Refusals);
        Assert.Contains("\"S9\"", refusal, StringComparison.Ordinal);
        Assert.Contains("did not print", refusal, StringComparison.Ordinal);
        Assert.Contains("S1, T1", refusal, StringComparison.Ordinal);
        Assert.Null(RunsOf(placed.Document)[0].Href);
    }

    [Fact]
    public void ALinkedRunLongerThanTheLimitIsRefused()
    {
        // The Stampli page: a 75-word paragraph as the link.
        var sentence = string.Join(' ', Enumerable.Range(1, GccDraftGuard.MaxLinkWords + 1).Select(i => $"word{i}"));
        var doc = Doc(Body("A", Text(Linked(sentence, "S1"))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        var refusal = Assert.Single(placed.Refusals);
        Assert.Contains($"{GccDraftGuard.MaxLinkWords + 1} words", refusal, StringComparison.Ordinal);
        Assert.Contains($"{GccDraftGuard.MaxLinkWords} words at most", refusal, StringComparison.Ordinal);
        Assert.Null(RunsOf(placed.Document)[0].Href);
    }

    [Fact]
    public void ALinkedRunAtTheLimitIsPlaced()
    {
        var words = string.Join(' ', Enumerable.Range(1, GccDraftGuard.MaxLinkWords).Select(i => $"w{i}"));
        var doc = Doc(Body("A", Text(new Run("Before "), Linked(words, "S1"), new Run(" after."))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        Assert.Equal(PartnerUrl, RunsOf(placed.Document)[1].Href);
    }

    [Fact]
    public void ALinkedRunWithNoWordsIsRefused()
    {
        var doc = Doc(Body("A", Text(Linked("  ", "T1"), new Run("Chaser chases."))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Contains("no words", Assert.Single(placed.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    public void AnHrefTheWriterTypedIsRefused()
    {
        // The field is gone from the contract. One that arrives anyway is the old defect, and is named.
        var doc = Doc(Body("A", new TextParagraph([new Run("Stampli's AP automation", Href: PartnerUrl)])));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        var refusal = Assert.Single(placed.Refusals);
        Assert.Contains("carries an href", refusal, StringComparison.Ordinal);
        Assert.Contains(PartnerUrl, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AnHrefAndAnIdOnTheSameRunIsRefusedOnceForTheHrefAndTheIdIsNotResolved()
    {
        var doc = Doc(Body("A", new TextParagraph([new Run("Chaser", Href: "https://chaserhq.com/", Link: "T1")])));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Contains("carries an href", Assert.Single(placed.Refusals), StringComparison.Ordinal);
        Assert.Equal("https://chaserhq.com/", RunsOf(placed.Document)[0].Href);
    }

    [Fact]
    public void ASectionHrefTheWriterTypedIsRefused()
    {
        var doc = Doc(new Section("h2", "A", [Text(new Run("Text."))], "https://elsewhere.test", []));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Contains("carries an href", Assert.Single(placed.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkInsideAQuotationIsRefused()
    {
        var doc = Doc(Body("A", new QuoteParagraph([Linked("Chaser", "T1"), new Run(" said so.")], "https://chaserhq.com/")));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Contains("quotation", Assert.Single(placed.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePagesOwnSchedulerLinkIsNotTheWriters()
    {
        // GccClosing builds the closing with the scheduler href before placement runs; it is the one
        // href a document may already carry.
        var doc = Doc(Body("Closing", new TextParagraph([new Run("Book a call", Href: Scheduler), new Run(".")])));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        Assert.Equal(Scheduler, RunsOf(placed.Document)[0].Href);
    }

    [Fact]
    public void ASecondLinkOnTheSchedulerWordsIsRefused()
    {
        var doc = Doc(Body("Closing", new TextParagraph([new Run("Book a call", Href: Scheduler, Link: "T1")])));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Contains("one link", Assert.Single(placed.Refusals), StringComparison.Ordinal);
        Assert.Equal(Scheduler, RunsOf(placed.Document)[0].Href);
    }

    [Fact]
    public void AToolPageIsHandedNoToolTargets()
    {
        // GccGenerateService builds a tool page's targets with tools: null, so a T# on it is refused
        // the way the guard would refuse the link -- before anything is spent on the guard.
        var evidenceOnly = GccLinkTargets.For([new GccQuoteablePage(PartnerUrl, "AP Automation", [], ["Text."])], tools: null);
        var doc = Doc(Body("A", Text(Linked("Chaser", "T1"), new Run(" chases."))));

        var placed = GccLinkPlacer.Place(doc, evidenceOnly, Scheduler);

        Assert.Contains("\"T1\"", Assert.Single(placed.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRefusalIsReportedNotJustTheFirst()
    {
        var doc = Doc(
            Body("A", Text(Linked("Ramp", "S9"), new Run(" saves money."))),
            Body("B", Text(Linked("Stampli", "S8"), new Run(" matches invoices."))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Equal(2, placed.Refusals.Count);
        Assert.Contains(placed.Refusals, r => r.Contains("\"A\"", StringComparison.Ordinal));
        Assert.Contains(placed.Refusals, r => r.Contains("\"B\"", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGuardAcceptsWhatThePlacerPlaces()
    {
        // The point of placing: the guard's link checks hold by construction, with the guard unchanged.
        var doc = Doc(Body("How it works",
            Text(new Run("Stampli's "), Linked("AP automation", "S1"), new Run(" matches invoices.")),
            Text(Linked("Chaser", "T1"), new Run(" chases late payers."))));
        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);
        Assert.Empty(placed.Refusals);

        var inputs = new GccGuardInputs(
            Provenance: null,
            RequiredTools: [],
            ConsultationHref: Scheduler,
            AllowedLinkUrls: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PartnerUrl },
            PublisherHosts: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            NumberEvidence: string.Empty,
            ToolBasePath: "/tools",
            ToolPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ToolPath });
        var verdict = GccDraftGuard.Pillar(placed.Document, inputs);

        Assert.DoesNotContain(verdict.Findings, f => f.Check is "links" or "link-text");
    }
}
