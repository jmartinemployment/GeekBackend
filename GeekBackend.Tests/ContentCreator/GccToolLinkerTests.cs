using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The writer links nothing; code puts each partner tool's page on the first mention of its name
/// (Jeff, 2026-10-10). Every way the writer used to get a link wrong -- the Stampli page's ten
/// paragraph-long links, the pillar's four, Ramp's URL from memory, the seven pages refused on
/// 2026-10-09 for anchor words copied inexactly, the "S#" placeholder in every FAQ answer -- needed
/// the writer to decide something. It decides nothing here, so nothing here refuses.
/// </summary>
public sealed class GccToolLinkerTests
{
    private const string RampPath = "/tools/accounting/accounts-payable/ramp";
    private const string BillPath = "/tools/accounting/accounts-payable/bill";

    private static readonly KnownCrawlTool Ramp = new("Ramp", null, RampPath);
    private static readonly KnownCrawlTool Bill = new("Bill", null, BillPath);

    private static Section Body(string heading, params Paragraph[] paragraphs) =>
        new("h2", heading, paragraphs, null, []);

    private static TextParagraph Text(params Run[] runs) => new(runs);

    private static TextParagraph Text(string text) => new([new Run(text)]);

    private static IReadOnlyList<Run> RunsOf(GccToolLinker.Linked linked, int section = 0, int paragraph = 0) =>
        ((TextParagraph)linked.Sections[section].Paragraphs[paragraph]).Runs;

    private static string Flat(IEnumerable<Section> sections) =>
        string.Join("|", sections.Select(s => s.Heading + ":" + string.Join("/", s.Paragraphs.Select(Flat)) + "{" + Flat(s.Children) + "}"));

    private static string Flat(Paragraph paragraph) => paragraph switch
    {
        TextParagraph t => string.Concat(t.Runs.Select(r => r.Text)),
        ListParagraph l => string.Join(";", l.Items.Select(i => string.Concat(i.Select(r => r.Text)))),
        QuoteParagraph q => string.Concat(q.Runs.Select(r => r.Text)),
        _ => string.Empty,
    };

    // ---- what is linked ---------------------------------------------------------------------------

    [Fact]
    public void TheFirstMentionIsLinkedOnTheNameAndNothingAroundIt()
    {
        var linked = GccToolLinker.Link(
            [Body("Where the hours go", Text("Teams that adopt Ramp close the month two days sooner."))], [Ramp]);

        var runs = RunsOf(linked);
        Assert.Equal(["Teams that adopt ", "Ramp", " close the month two days sooner."], runs.Select(r => r.Text));
        Assert.Equal([null, RampPath, null], runs.Select(r => r.Href));
        var link = Assert.Single(linked.Links);
        Assert.Equal(new GccToolLinker.ToolLink("Ramp", RampPath, "Where the hours go", "Ramp"), link);
        Assert.Empty(linked.NotLinked);
    }

    [Fact]
    public void ANameAtTheStartOrTheEndOfARunLeavesNoEmptyRunBesideIt()
    {
        var linked = GccToolLinker.Link([Body("A", Text("Ramp pays the bill."), Text("The team chose Ramp"))], [Ramp]);

        Assert.Equal(["Ramp", " pays the bill."], RunsOf(linked).Select(r => r.Text));
        // One link per tool per page: the second paragraph is the second mention.
        Assert.Equal(["The team chose Ramp"], RunsOf(linked, paragraph: 1).Select(r => r.Text));
    }

    [Fact]
    public void OnlyTheFirstMentionOnThePageIsLinked()
    {
        var linked = GccToolLinker.Link(
            [
                Body("One", Text("Ramp issues the card. Ramp also codes the spend.")),
                Body("Two", Text("Later, Ramp syncs to the ledger.")),
            ],
            [Ramp]);

        Assert.Equal(["Ramp", " issues the card. Ramp also codes the spend."], RunsOf(linked).Select(r => r.Text));
        Assert.All(RunsOf(linked, section: 1), run => Assert.Null(run.Href));
        Assert.Single(linked.Links);
    }

    [Fact]
    public void APossessiveOrPunctuationAfterTheNameStillLinksTheNameAlone()
    {
        var possessive = GccToolLinker.Link([Body("A", Text("Using Ramp's approval rules, a manager signs off."))], [Ramp]);
        var trailing = GccToolLinker.Link([Body("A", Text("The card program runs on Ramp."))], [Ramp]);

        Assert.Equal(["Using ", "Ramp", "'s approval rules, a manager signs off."], RunsOf(possessive).Select(r => r.Text));
        Assert.Equal(["The card program runs on ", "Ramp", "."], RunsOf(trailing).Select(r => r.Text));
    }

    [Fact]
    public void TheWritersOwnCapitalsAreKeptUnderTheLink()
    {
        // The partner list spells it "Approvalmax"; the writer wrote "ApprovalMax" (the 16:52 run of 2026-10-06).
        var linked = GccToolLinker.Link(
            [Body("A", Text("ApprovalMax routes each invoice."))], [new KnownCrawlTool("Approvalmax", null, "/tools/a/approvalmax")]);

        var run = RunsOf(linked)[0];
        Assert.Equal("ApprovalMax", run.Text);
        Assert.Equal("/tools/a/approvalmax", run.Href);
        Assert.Equal("ApprovalMax", Assert.Single(linked.Links).Words);
    }

    [Fact]
    public void AnOrdinaryWordThatSpellsAToolIsNotLinked()
    {
        // "a bill arrives" does not name Bill: the one matcher every check reads (GccRequiredToolMentions).
        var linked = GccToolLinker.Link([Body("A", Text("When a bill arrives, someone keys it. Billing waits."))], [Bill]);

        Assert.Equal(["When a bill arrives, someone keys it. Billing waits."], RunsOf(linked).Select(r => r.Text));
        Assert.Empty(linked.Links);
        Assert.Equal(["Bill: not named in the body"], linked.NotLinked);
    }

    [Fact]
    public void ALongerNameIsLinkedWholeBeforeAShorterNameItContains()
    {
        var billDotCom = new KnownCrawlTool("Bill.com", null, "/tools/a/bill-com");
        var linked = GccToolLinker.Link(
            [Body("A", Text("Bill.com pays vendors. Bill is its newer name."))], [Bill, billDotCom]);

        var runs = RunsOf(linked);
        Assert.Equal(["Bill.com", " pays vendors. ", "Bill", " is its newer name."], runs.Select(r => r.Text));
        Assert.Equal(["/tools/a/bill-com", null, BillPath, null], runs.Select(r => r.Href));
    }

    [Fact]
    public void EachToolIsLinkedOnce()
    {
        var linked = GccToolLinker.Link([Body("A", Text("Ramp and Bill both sync to the ledger."))], [Ramp, Bill]);

        var runs = RunsOf(linked);
        Assert.Equal(["Ramp", " and ", "Bill", " both sync to the ledger."], runs.Select(r => r.Text));
        Assert.Equal([RampPath, null, BillPath, null], runs.Select(r => r.Href));
        Assert.Equal(["Ramp", "Bill"], linked.Links.Select(l => l.Tool).OrderByDescending(n => n));
    }

    // ---- where a link can sit ---------------------------------------------------------------------

    [Fact]
    public void AListItemIsLinked()
    {
        var list = new ListParagraph(false, [[new Run("Keyed by hand")], [new Run("Coded by Ramp on capture")]]);

        var linked = GccToolLinker.Link([Body("A", list)], [Ramp]);

        var items = ((ListParagraph)linked.Sections[0].Paragraphs[0]).Items;
        Assert.Equal(["Keyed by hand"], items[0].Select(r => r.Text));
        Assert.Equal(["Coded by ", "Ramp", " on capture"], items[1].Select(r => r.Text));
        Assert.Equal(RampPath, items[1][1].Href);
    }

    [Fact]
    public void ANestedSubsectionIsLinkedAfterItsParentsOwnParagraphs()
    {
        var child = new Section("h3", "Child", [Text("Ramp is named here first in reading order? No.")], null, []);
        var parent = new Section("h2", "Parent", [Text("The parent names Ramp first.")], null, [child]);

        var linked = GccToolLinker.Link([parent], [Ramp]);

        Assert.Equal("Parent", Assert.Single(linked.Links).Heading);
        Assert.All(((TextParagraph)linked.Sections[0].Children[0].Paragraphs[0]).Runs, run => Assert.Null(run.Href));
    }

    [Fact]
    public void AMentionOnlyInASubsectionIsLinkedThereUnderItsOwnHeading()
    {
        var child = new Section("h3", "How the card works", [Text("Ramp issues it.")], null, []);
        var parent = new Section("h2", "Parent", [Text("Nothing named here.")], null, [child]);

        var linked = GccToolLinker.Link([parent], [Ramp]);

        Assert.Equal("How the card works", Assert.Single(linked.Links).Heading);
        Assert.Equal(RampPath, ((TextParagraph)linked.Sections[0].Children[0].Paragraphs[0]).Runs[0].Href);
    }

    [Fact]
    public void AQuotationIsNeverLinked()
    {
        var quote = new QuoteParagraph([new Run("Ramp saved us forty hours a month.")], "https://ramp.com/customers");

        var linked = GccToolLinker.Link([Body("A", quote, Text("That is Ramp's own customer."))], [Ramp]);

        Assert.Same(quote, linked.Sections[0].Paragraphs[0]);
        Assert.Equal(RampPath, RunsOf(linked, paragraph: 1)[1].Href);
    }

    [Fact]
    public void ARunThatAlreadyCarriesAnHrefIsLeftAlone()
    {
        var scheduler = new Run("Ask Ramp about it when you book", Href: "#consultationAppointment2xl");

        var linked = GccToolLinker.Link([Body("A", Text(scheduler), Text("Ramp is named again."))], [Ramp]);

        Assert.Equal([scheduler], RunsOf(linked));
        Assert.Equal(RampPath, RunsOf(linked, paragraph: 1)[0].Href);
    }

    [Fact]
    public void AHeadingIsNeverLinkedBecauseOnlyParagraphsAreRead()
    {
        var linked = GccToolLinker.Link([Body("Ramp for approvals", Text("Approvals stall at the manager."))], [Ramp]);

        Assert.Equal("Ramp for approvals", linked.Sections[0].Heading);
        Assert.Null(linked.Sections[0].Href);
        Assert.Equal(["Ramp: not named in the body"], linked.NotLinked);
    }

    // ---- what is reported, and what never changes ---------------------------------------------------

    [Fact]
    public void AToolWithNoPublicPathIsReportedAndNotLinked()
    {
        var linked = GccToolLinker.Link([Body("A", Text("Ramp issues the card."))], [new KnownCrawlTool("Ramp", "https://ramp.com", null)]);

        Assert.All(RunsOf(linked), run => Assert.Null(run.Href));
        Assert.Equal(["Ramp: no public path for its tool page"], linked.NotLinked);
    }

    [Fact]
    public void NoToolsMeansNothingIsLinkedAndNothingIsReported()
    {
        var sections = new[] { Body("A", Text("Ramp issues the card.")) };

        var linked = GccToolLinker.Link(sections, []);

        Assert.Empty(linked.Links);
        Assert.Empty(linked.NotLinked);
        Assert.Equal(Flat(sections), Flat(linked.Sections));
    }

    [Fact]
    public void TheWordsOfThePageAreNeverChanged()
    {
        var sections = new[]
        {
            Body("One",
                Text(new Run("Ramp's cards, "), new Run("Bill"), new Run("'s payments and the rest.")),
                new ListParagraph(true, [[new Run("First, Ramp.")], [new Run("Then Bill.")]]),
                new QuoteParagraph([new Run("Bill pays on time.")], "https://bill.com")),
            new Section("h2", "Two", [Text("Nothing here.")], null,
                [new Section("h3", "Deeper", [Text("Ramp again, and Bill again.")], null, [])]),
        };

        var linked = GccToolLinker.Link(sections, [Ramp, Bill]);

        Assert.Equal(Flat(sections), Flat(linked.Sections));
        Assert.Equal(2, linked.Links.Count);
    }

    [Fact]
    public void ALinkedNameIsWellInsideTheGuardsWordLimit()
    {
        // The guard's link-text check now holds code to the limit, not the model: a linked run is a name.
        var linked = GccToolLinker.Link([Body("A", Text("Ramp issues the card."))], [Ramp]);

        Assert.All(linked.Links, link => Assert.True(GccDraftGuard.WordCount(link.Words) <= GccDraftGuard.MaxLinkWords));
    }
}
