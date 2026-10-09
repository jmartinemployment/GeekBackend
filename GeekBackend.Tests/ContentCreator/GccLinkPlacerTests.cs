using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The writer names a target and the words; the code puts the link there. Every way the writer used
/// to get a link wrong -- the Stampli page's ten paragraph-long links, the pillar's four, Ramp's URL
/// from memory (2026-10-09) -- is a refusal here by name, and nothing is repaired.
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

    private static TextParagraph Text(string text, params LinkRef[] links) =>
        new([new Run(text)], links.Length == 0 ? null : links);

    private static IReadOnlyList<Run> RunsOf(ContentDocument document, int section = 0, int paragraph = 0) =>
        ((TextParagraph)document.Sections[section].Paragraphs[paragraph]).Runs;

    // ---- placing ------------------------------------------------------------------------------------

    [Fact]
    public void TheLinkLandsOnTheAnchorWordsAndNothingElse()
    {
        var doc = Doc(Body("How it works",
            Text("Stampli's AP automation matches invoices to purchase orders.", new LinkRef("S1", "AP automation"))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        var runs = RunsOf(placed.Document);
        Assert.Equal(["Stampli's ", "AP automation", " matches invoices to purchase orders."], runs.Select(r => r.Text));
        Assert.Equal([null, PartnerUrl, null], runs.Select(r => r.Href));
        // The words are the writer's, untouched: joined back they are the original sentence.
        Assert.Equal("Stampli's AP automation matches invoices to purchase orders.", string.Concat(runs.Select(r => r.Text)));
    }

    [Fact]
    public void AnAnchorAtTheStartOrEndLeavesNoEmptyRun()
    {
        var doc = Doc(Body("A", Text("Chaser chases.", new LinkRef("T1", "Chaser"))), Body("B", Text("Try Chaser", new LinkRef("T1", "Chaser"))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        Assert.Equal(["Chaser", " chases."], RunsOf(placed.Document, 0).Select(r => r.Text));
        Assert.Equal(["Try ", "Chaser"], RunsOf(placed.Document, 1).Select(r => r.Text));
        Assert.Equal(ToolPath, RunsOf(placed.Document, 0)[0].Href);
    }

    [Fact]
    public void TwoLinksInOneParagraphAreBothPlaced()
    {
        var doc = Doc(Body("A",
            Text("Chaser and AP automation together.", new LinkRef("T1", "Chaser"), new LinkRef("S1", "AP automation"))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        var runs = RunsOf(placed.Document);
        Assert.Equal(["Chaser", " and ", "AP automation", " together."], runs.Select(r => r.Text));
        Assert.Equal([ToolPath, null, PartnerUrl, null], runs.Select(r => r.Href));
    }

    [Fact]
    public void TheFirstOccurrenceIsLinked()
    {
        var doc = Doc(Body("A", Text("Chaser does what Chaser does.", new LinkRef("T1", "Chaser"))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        Assert.Equal(["Chaser", " does what Chaser does."], RunsOf(placed.Document).Select(r => r.Text));
    }

    [Fact]
    public void AListItemTakesTheLinkInTheItemThatHasTheWords()
    {
        var list = new ListParagraph(false,
            [[new Run("Invoices are matched.")], [new Run("Chaser sends the reminders.")]],
            [new LinkRef("T1", "Chaser")]);
        var doc = Doc(Body("A", list));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        var items = ((ListParagraph)placed.Document.Sections[0].Paragraphs[0]).Items;
        Assert.Equal(["Invoices are matched."], items[0].Select(r => r.Text));
        Assert.Equal(["Chaser", " sends the reminders."], items[1].Select(r => r.Text));
        Assert.Equal(ToolPath, items[1][0].Href);
    }

    [Fact]
    public void PlacedParagraphsCarryNoLinksField()
    {
        var doc = Doc(Body("A", Text("Chaser chases.", new LinkRef("T1", "Chaser"))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Null(((TextParagraph)placed.Document.Sections[0].Paragraphs[0]).Links);
    }

    [Fact]
    public void ChildrenAndTheLedeArePlacedToo()
    {
        var child = new Section("h3", "Child", [Text("Chaser here.", new LinkRef("T1", "Chaser"))], null, []);
        var doc = new ContentDocument(
            new Section("h2", "Opening", [Text("AP automation first.", new LinkRef("S1", "AP automation"))], null, []),
            [new Section("h2", "Parent", [], null, [child])]);

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        Assert.Equal(PartnerUrl, ((TextParagraph)placed.Document.Lede.Paragraphs[0]).Runs[0].Href);
        Assert.Equal(ToolPath, ((TextParagraph)placed.Document.Sections[0].Children[0].Paragraphs[0]).Runs[0].Href);
    }

    [Fact]
    public void AParagraphWithNoLinksIsUnchanged()
    {
        var doc = Doc(Body("A", Text("Nothing to link here.")));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        Assert.Equal(["Nothing to link here."], RunsOf(placed.Document).Select(r => r.Text));
    }

    // ---- refusing -----------------------------------------------------------------------------------

    [Fact]
    public void ATargetThePromptDidNotPrintIsRefusedAndTheTargetsAreNamed()
    {
        // Ramp, 2026-10-09: a URL from memory. There is no field for one now, and an id that was not
        // printed is the same thing said another way.
        var doc = Doc(Body("A", Text("Ramp saves money.", new LinkRef("S9", "Ramp"))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        var refusal = Assert.Single(placed.Refusals);
        Assert.Contains("\"S9\"", refusal, StringComparison.Ordinal);
        Assert.Contains("did not print", refusal, StringComparison.Ordinal);
        Assert.Contains("S1, T1", refusal, StringComparison.Ordinal);
        Assert.Null(RunsOf(placed.Document)[0].Href);
    }

    [Fact]
    public void AnAnchorLongerThanTheLimitIsRefused()
    {
        // The Stampli page: a 75-word paragraph as the link. The words are the whole paragraph here.
        var sentence = string.Join(' ', Enumerable.Range(1, GccDraftGuard.MaxLinkWords + 1).Select(i => $"word{i}"));
        var doc = Doc(Body("A", Text(sentence, new LinkRef("S1", sentence))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        var refusal = Assert.Single(placed.Refusals);
        Assert.Contains($"{GccDraftGuard.MaxLinkWords + 1} words", refusal, StringComparison.Ordinal);
        Assert.Contains($"{GccDraftGuard.MaxLinkWords} words at most", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnchorAtTheLimitIsPlaced()
    {
        var anchor = string.Join(' ', Enumerable.Range(1, GccDraftGuard.MaxLinkWords).Select(i => $"w{i}"));
        var doc = Doc(Body("A", Text($"Before {anchor} after.", new LinkRef("S1", anchor))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Empty(placed.Refusals);
        Assert.Equal(PartnerUrl, RunsOf(placed.Document)[1].Href);
    }

    [Fact]
    public void AnchorWordsNotInTheParagraphAreRefused()
    {
        var doc = Doc(Body("A", Text("Stampli matches invoices.", new LinkRef("S1", "AP automation"))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        var refusal = Assert.Single(placed.Refusals);
        Assert.Contains("\"AP automation\"", refusal, StringComparison.Ordinal);
        Assert.Contains("does not appear in the paragraph", refusal, StringComparison.Ordinal);
        Assert.Contains("Stampli matches invoices.", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyAnchorIsRefused()
    {
        var doc = Doc(Body("A", Text("Chaser chases.", new LinkRef("T1", "  "))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Contains("names no anchor words", Assert.Single(placed.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnchorAcrossTwoRunsIsRefused()
    {
        var doc = Doc(Body("A", new TextParagraph(
            [new Run("Chaser "), new Run("chases.")],
            [new LinkRef("T1", "Chaser chases")])));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Contains("crosses a run boundary", Assert.Single(placed.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    public void OverlappingAnchorsAreRefusedNotStacked()
    {
        var doc = Doc(Body("A", Text("Chaser chases.", new LinkRef("T1", "Chaser"), new LinkRef("S1", "Chaser"))));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Contains("already linked", Assert.Single(placed.Refusals), StringComparison.Ordinal);
        Assert.Equal(ToolPath, RunsOf(placed.Document)[0].Href);
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
    public void ASectionHrefTheWriterTypedIsRefused()
    {
        var doc = Doc(new Section("h2", "A", [Text("Text.")], "https://elsewhere.test", []));

        var placed = GccLinkPlacer.Place(doc, Targets, Scheduler);

        Assert.Contains("carries an href", Assert.Single(placed.Refusals), StringComparison.Ordinal);
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
    public void AToolPageIsHandedNoToolTargets()
    {
        // GccGenerateService builds a tool page's targets with tools: null, so a T# on it is refused
        // the way the guard would refuse the link -- before anything is spent on the guard.
        var evidenceOnly = GccLinkTargets.For([new GccQuoteablePage(PartnerUrl, "AP Automation", [], ["Text."])], tools: null);
        var doc = Doc(Body("A", Text("Chaser chases.", new LinkRef("T1", "Chaser"))));

        var placed = GccLinkPlacer.Place(doc, evidenceOnly, Scheduler);

        Assert.Contains("\"T1\"", Assert.Single(placed.Refusals), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRefusalIsReportedNotJustTheFirst()
    {
        var doc = Doc(
            Body("A", Text("Ramp saves money.", new LinkRef("S9", "Ramp"))),
            Body("B", Text("Stampli matches invoices.", new LinkRef("S1", "AP automation"))));

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
            Text("Stampli's AP automation matches invoices.", new LinkRef("S1", "AP automation")),
            Text("Chaser chases late payers.", new LinkRef("T1", "Chaser"))));
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
