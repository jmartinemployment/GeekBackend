using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The operator's own framing of a niche: the problem, how it is failed, and what automation answers it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The brief had no problem field. On a <c>problem_solution</c> angle the
/// opening slot asks for <i>"the problem this reader has with {keyword} today, what it costs them, and
/// where {product} breaks it"</i> (<c>ToolPrompts.cs</c>) with <c>Guidance: null</c> — so the writer
/// invented all three. Jeff researches exactly those three things per niche, by hand, and the answer
/// was being read and then discarded.
/// </para>
/// <para>
/// <b>Framing, not evidence.</b> This is what the page should <i>argue</i>. It is not a citation source
/// and never enters the corpus: quotes and vendor claims still come from retrieved, verified crawl text.
/// A page may say the problem is late invoices because the operator says so; it may not attribute a
/// capability to a product on this basis.
/// </para>
/// <para>
/// <b>Affiliate economics are deliberately absent.</b> Jeff, 2026-10-02: <i>"Partner program text has
/// no place in my output."</i> There is no field for a commission rate or payout cap, and that is the
/// enforcement — <c>BriefJson</c> is prompt input (<c>ExtractBriefFields</c> reads it,
/// <c>BuildBriefAndResearchBlock</c> feeds it to every long-form prompt), so a figure stored there sits
/// one field addition away from a page. Absence cannot regress; a test can.
/// </para>
/// </remarks>
/// <param name="CoreProblem">The problem the reader has today, in the operator's words.</param>
/// <param name="PainPoints">Where it goes wrong, one entry per failure — the body's substance. Each
/// entry may be a full paragraph; the operator's research states a failure as a lead plus its
/// explanation, and a fragment is not something a writer can argue from.</param>
/// <param name="AutomationToPitch">What removes the problem, and the shape of the offer.</param>
public sealed record GccNicheFraming(
    string CoreProblem,
    IReadOnlyList<string> PainPoints,
    string AutomationToPitch)
{
    /// <summary>
    /// One retrieval question per failure (Geek-Crawler-Rag plans/retrieval-from-the-brief.md,
    /// 2026-10-08): the reader's problem, the vendor's solution in the vendor's own vocabulary, and
    /// a few of the vendor's terms. Retrieval input, never evidence and never quoted: the solution
    /// goes to the index's meaning half and the terms to its keyword half. Empty when the brief
    /// carries none.
    /// <para>
    /// One use besides retrieval: a row's failure and its remedy are told to the writers as the
    /// operator's note of what answers which failure (<see cref="GccPartnerAnswers"/>). A partner's
    /// own rows reached the pillar and the blog from 2026-10-11; since 2026-10-12 every row reaches
    /// every page that is about its partner, the tool's own page included, and a row saved for all
    /// partners reaches all of them. Its terms are for the search alone.
    /// </para>
    /// </summary>
    public IReadOnlyList<GccEvidenceRow> Evidence { get; init; } = [];

    /// <summary>
    /// Where it goes wrong as this set holds it, each failure once and in the operator's order, with
    /// the remedy noted for it where a row carries one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A failure is entered as a row since 2026-10-08, and the form fills <see cref="PainPoints"/>
    /// from the rows' failures on every change, so a brief saved since then holds each failure twice:
    /// once as a paragraph and once on its row. An older brief may hold paragraphs with no row, and a
    /// brief written by hand may hold rows with no paragraph. This is the one reading of both: the
    /// paragraphs in order, each with its row's remedy when a row states it, then the rows no
    /// paragraph states.
    /// </para>
    /// <para>
    /// A row's failure that runs to more than one paragraph is several entries of
    /// <see cref="PainPoints"/> and one failure here
    /// (<see cref="GccNicheFramingReader.IsRowsFailure"/>).
    /// </para>
    /// </remarks>
    public IReadOnlyList<GccFailure> Failures()
    {
        var rows = Evidence.Where(row => row.Problem.Length > 0 || row.Solution.Length > 0).ToList();
        var listed = new bool[rows.Count];
        var failures = new List<GccFailure>();
        foreach (var pain in PainPoints)
        {
            var at = rows.FindIndex(row => GccNicheFramingReader.IsRowsFailure(pain, row));
            if (at < 0)
            {
                failures.Add(new GccFailure(pain, string.Empty));
                continue;
            }

            if (listed[at]) continue;
            listed[at] = true;
            failures.Add(new GccFailure(rows[at].Problem, rows[at].Solution));
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (!listed[i]) failures.Add(new GccFailure(rows[i].Problem, rows[i].Solution));
        }

        return failures;
    }

    /// <summary>
    /// True when the failures this page argues are the ones the operator entered under the partners:
    /// none was entered for the category, and the page is shown the partners' in
    /// <see cref="GccPartnerAnswers"/>. Set only by <see cref="GccNicheFramingReader.ForEveryPartner"/>,
    /// for the pillar and the blog.
    /// </summary>
    /// <remarks>
    /// The operator's research is per partner, so a brief entered that way has no failure for the
    /// category. Until 2026-10-11 the sections that argue where the work goes wrong were then told
    /// none and the writer chose its own, the thing the framing exists to stop.
    /// </remarks>
    public bool FailuresAreUnderThePartners { get; init; }

    /// <summary>True when any part carries something. An all-blank set is not framing.</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(CoreProblem)
        || PainPoints.Count > 0
        || !string.IsNullOrWhiteSpace(AutomationToPitch)
        || Evidence.Count > 0
        || FailuresAreUnderThePartners;

    /// <summary>
    /// The three parts as section guidance, or null when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Labelled as the operator's framing on purpose. Without the label the writer cannot tell this
    /// apart from retrieved evidence and may attribute it to a source, which is the one way this
    /// becomes a citation problem. The instruction to argue rather than cite is part of the payload,
    /// not a separate prompt line that could be added to one call site and missed on another.
    /// </remarks>
    public string? ToGuidance() =>
        Render("Where it goes wrong. Cover these in your own prose, never as a list, and never verbatim:");

    /// <summary>
    /// The three parts for a short piece -- an email or a social post -- or null when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// The same framing the pages argue (Jeff, 2026-10-10: "They are also marketing material"), with the
    /// one instruction a short piece cannot follow changed. A 150-word email told to cover every failure
    /// either ignores the instruction or stops being an email, so it is told to build on one and leave
    /// the rest. Until then the email and the social post were given none of the operator's framing.
    /// </remarks>
    public string? ShortFormGuidance() =>
        Render("Where it goes wrong. This piece is short: build it on the one of these that serves its " +
            "single idea, in your own words and never verbatim, and leave the rest out:");

    /// <summary>
    /// How the framing is headed wherever it is printed whole. A slide told to build on it is sent
    /// here by this name (<c>LinkedInDocumentPrompts</c>), so the two are one string.
    /// </summary>
    public const string FramingHeading = "THE OPERATOR'S OWN FRAMING OF THIS PROBLEM";

    /// <summary>How the line that carries the operator's answer begins, for the same reason.</summary>
    public const string AnswerLabel = "What answers it";

    /// <summary>
    /// The three parts for a deck of slides ("PDF / LinkedIn document"), or null when there is nothing
    /// to say.
    /// </summary>
    /// <remarks>
    /// The same framing the pages argue. A page is told to cover every failure in its prose; a slide is
    /// two to four short lines, so a deck is shown every failure and told to build on the ones that
    /// cost the reader most, as a short piece is told to build on one
    /// (<see cref="ShortFormGuidance"/>). Nothing is left out of what the writer is shown.
    /// </remarks>
    public string? DeckGuidance() =>
        Render("Where it goes wrong. A slide is a few short lines, so the deck does not list every one of " +
            "these: the slide on what goes wrong today is built on the ones that cost this reader most, in " +
            "your own words and never verbatim, and it names no failure that is not here:");

    /// <summary>
    /// The problem alone, for the call that writes a page's hook. Null when the operator stated none.
    /// </summary>
    /// <remarks>
    /// A blog and a tool page are opened by a call of their own, a few sentences long, written before
    /// the body. The framing sits on the first body section, so until 2026-10-10 that call was told
    /// nothing of the problem the operator named and could open on another, which the section after it
    /// then had to turn away from. The failures and the answer stay where they are: a hook told to
    /// cover them is no longer a hook. The pillar's opening is an introduction as well as a hook, and
    /// carries the whole framing (<see cref="ToGuidance"/>).
    /// </remarks>
    public string? HookGuidance() => HookFor(CoreProblem, product: null, ownPainPoint: null);

    /// <summary>
    /// The hook's guidance for the problem as the operator stated it: for the whole category, for one
    /// tool, or both. Null when neither is stated.
    /// </summary>
    /// <remarks>
    /// One wording for the pages opened on the category's problem (<see cref="HookGuidance"/>) and
    /// for a tool page, which is told the category's and its tool's own
    /// (<see cref="GccToolFraming.HookGuidance"/>).
    /// </remarks>
    internal static string? HookFor(string? problem, string? product, string? ownPainPoint)
    {
        var stated = (problem ?? string.Empty).Trim();
        var own = (ownPainPoint ?? string.Empty).Trim();
        if (string.Equals(stated, own, StringComparison.OrdinalIgnoreCase)) own = string.Empty;
        if (stated.Length == 0 && own.Length == 0) return null;

        var sb = new StringBuilder();
        sb.Append("THE OPERATOR'S OWN STATEMENT OF THE PROBLEM THIS PAGE IS ABOUT -- the hook is about this ");
        sb.Append("problem and no other, in your own words and never verbatim. It is the publisher's ");
        sb.Append("position, not retrieved evidence: do not attribute it to a source, and do not present it ");
        sb.Append("as something a vendor said. Where it goes wrong and what answers it belong to the ");
        sb.Append("sections that follow; the hook does not list them.");
        if (own.Length == 0 || stated.Length == 0)
        {
            sb.AppendLine();
            sb.Append("The problem: ").Append(stated.Length > 0 ? stated : own);
            return sb.ToString();
        }

        // Both, since 2026-10-12: the operator states one problem for every partner and a pain point
        // for each tool, and until then a tool page's hook was told the tool's own in the category's place.
        var name = string.IsNullOrWhiteSpace(product) ? "This product" : product.Trim();
        sb.AppendLine();
        sb.Append("The problem: ").Append(stated);
        sb.AppendLine();
        sb.Append(name).Append("'s own pain point: ").Append(own);
        sb.AppendLine();
        sb.Append("The hook opens on ").Append(name).Append("'s own pain point and stays inside the problem.");
        return sb.ToString();
    }

    private string? Render(string failuresInstruction)
    {
        // Only what this prints counts. A set that carries rows and nothing else, or only the word that
        // its failures are under the partners, has no line to put under the heading below.
        if (string.IsNullOrWhiteSpace(CoreProblem)
            && PainPoints.Count == 0
            && string.IsNullOrWhiteSpace(AutomationToPitch))
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append(FramingHeading).Append(" -- argue from it, never cite it. ");
        sb.Append("It is the publisher's position, not retrieved evidence: do not attribute any of it ");
        sb.Append("to a source, and do not present it as something a vendor said.");

        if (!string.IsNullOrWhiteSpace(CoreProblem))
        {
            sb.AppendLine();
            sb.Append("The problem: ").Append(CoreProblem.Trim());
        }

        if (PainPoints.Count > 0)
        {
            // One per line, numbered. These were joined on " | ", which was fine for three terse points
            // and unreadable for the fourteen a real category query returns -- nine tabular failures plus
            // five consequence paragraphs, pipe-joined into a single wall of text the model has to parse
            // before it can use any of it.
            sb.AppendLine();
            sb.Append(failuresInstruction);
            for (var i = 0; i < PainPoints.Count; i++)
            {
                sb.AppendLine();
                sb.Append(i + 1).Append(". ").Append(PainPoints[i]);
            }
        }

        if (!string.IsNullOrWhiteSpace(AutomationToPitch))
        {
            sb.AppendLine();
            sb.Append(AnswerLabel).Append(": ").Append(AutomationToPitch.Trim());
        }

        return sb.ToString();
    }

    /// <summary>
    /// The failures alone, for the section whose job is what is going wrong today. Null when the
    /// operator listed none.
    /// </summary>
    /// <remarks>
    /// The pillar and the blog each have a slot for "what is actually going wrong in this work today",
    /// and until 2026-10-06 it carried no guidance, so the writer filled it from retrieved vendor
    /// prose and competitor coverage -- a methodology of its own. Jeff, 2026-10-06: "It is again
    /// inventing its own Methodology versus using mine?" This section's substance is the operator's
    /// failures, every one of them and no other.
    /// <para>
    /// When the operator entered the failures under the partners and none for the category
    /// (<see cref="FailuresAreUnderThePartners"/>), the section is sent to them where the page already
    /// lists them, under each partner. It is not told to cover every one: five partners with five
    /// failures each are twenty-five, several of them one failure in different words, and the blog
    /// has one section of 450 to 600 words for them. It is still told to add none of its own.
    /// </para>
    /// </remarks>
    public string? FailuresGuidance()
    {
        if (PainPoints.Count == 0) return FailuresAreUnderThePartners ? PartnersFailuresGuidance : null;

        var sb = new StringBuilder();
        sb.Append("THE OPERATOR'S OWN FRAMING -- argue from it, never cite it; it is the publisher's position, ");
        sb.Append("not retrieved evidence, so attribute none of it to a source. ");
        sb.Append("This section's substance is where the work goes wrong, in the operator's own analysis. Cover ");
        sb.Append("every one of these, in your own prose, never as a list and never verbatim, and add no failure ");
        sb.Append("the operator did not name:");
        for (var i = 0; i < PainPoints.Count; i++)
        {
            sb.AppendLine();
            sb.Append(i + 1).Append(". ").Append(PainPoints[i]);
        }

        return sb.ToString();
    }

    private const string PartnersFailuresGuidance =
        "THE OPERATOR'S OWN FRAMING -- argue from it, never cite it; it is the publisher's position, "
        + "not retrieved evidence, so attribute none of it to a source. "
        + "This section's substance is where the work goes wrong, in the operator's own analysis. The operator "
        + "entered those failures under the partner that answers each: they are the \"" + GccPartnerAnswers.FailureLabel
        + "\" lines in " + GccPartnerAnswers.Heading + ". Argue this section from them, in your own prose, never "
        + "as a list and never verbatim, and add no failure the operator did not name. Failures that say the same "
        + "thing under different partners are one failure.";

    /// <summary>
    /// The automation alone, for the sections about how the approach works, what makes it hold up and
    /// what rolling it out involves. Null when the operator described none.
    /// </summary>
    /// <remarks>
    /// "How the approach works end to end: the mechanics" was answered, until 2026-10-06, with whatever
    /// approach the retrieved passages described -- a vendor's, or the aggregate of the pages already
    /// ranking. The approach is the operator's; the writer supplies the prose and the evidence for it.
    /// </remarks>
    public string? ApproachGuidance()
    {
        if (string.IsNullOrWhiteSpace(AutomationToPitch)) return null;

        return "THE OPERATOR'S OWN FRAMING -- argue from it, never cite it; it is the publisher's position, "
            + "not retrieved evidence, so attribute none of it to a source. The approach this section describes "
            + "is the operator's, not a generic one and not a vendor's: " + AutomationToPitch.Trim()
            + " Describe its mechanics, its sequence and its decisions. The partner tools appear where they do "
            + "this work, as the evidence shows them doing it.";
    }

    /// <summary>A one-line pointer for the later sections, so the approach is stated once and held to throughout.</summary>
    public string? ApproachPointer() =>
        string.IsNullOrWhiteSpace(AutomationToPitch)
            ? null
            : "Within the operator's own approach, stated in the section before this one -- never a generic "
              + "or a vendor's alternative to it.";
}

/// <summary>
/// One failure the operator named, with the remedy noted for it. Either may be empty, never both: a
/// failure entered with no remedy, or a remedy entered on a row that names no failure.
/// </summary>
public sealed record GccFailure(string Problem, string Remedy);

/// <summary>
/// What the operator entered for one tool: the framing for the whole category, and that tool's own
/// section of the brief. Both are kept, and neither stands in for the other.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why two sets and not one merged.</b> Until 2026-10-12 a tool's own problem and its own pitch
/// replaced the category's, on the reasoning that two core problems on one page are incoherent. So
/// a partner's site was searched with one of the two problems the operator entered, and its page was
/// told one of the two problems and one of the two pitches. Jeff, 2026-10-12: "it is not an OR it is
/// both PAIN POINTS". He researches one problem for every partner and a pain point for each tool,
/// enters both, and both are searched for and told.
/// </para>
/// <para>
/// <b>A separate type on purpose.</b> <see cref="GccNicheFraming"/>'s own guidance prints one set.
/// Handing it a tool's merged set is how a box came to be entered and not used; a caller holding
/// this cannot print the category's and forget the tool's, because there is no method here that does.
/// </para>
/// </remarks>
/// <param name="Category">What was entered for the whole category, or null when nothing was.</param>
/// <param name="Own">What was entered in this tool's own section, merged with nothing, or null when
/// nothing was or the tool is no partner of the project.</param>
public sealed record GccToolFraming(GccNicheFraming? Category, GccNicheFraming? Own)
{
    /// <summary>
    /// Every statement of the problem, each once: the tool's own "Core pain point", then the
    /// category's "The core problem". What a partner's site is searched with.
    /// </summary>
    /// <remarks>
    /// The tool's own first because it was this tool's first search before the category's was added
    /// beside it: the pages it finds keep their place in the tool's research.
    /// </remarks>
    public IReadOnlyList<string> Problems
    {
        get
        {
            var problems = new List<string>(2);
            foreach (var problem in new[] { Own?.CoreProblem, Category?.CoreProblem })
            {
                var stated = (problem ?? string.Empty).Trim();
                if (stated.Length > 0 && !problems.Contains(stated, StringComparer.OrdinalIgnoreCase)) problems.Add(stated);
            }

            return problems;
        }
    }

    /// <summary>
    /// The rows a partner's site is searched with: the category's, then the tool's own. A tool's row
    /// that restates a category row's failure is searched in that row's place, because the tool's
    /// remedy for that failure is the one its own site is asked about.
    /// </summary>
    /// <remarks>
    /// A row that names no failure restates none. It used to take the place of the category's first
    /// row with no failure, which the form's own list of a tool's searches never did.
    /// </remarks>
    public IReadOnlyList<GccEvidenceRow> RowsToSearch
    {
        get
        {
            var rows = new List<GccEvidenceRow>(Category?.Evidence ?? []);
            foreach (var row in Own?.Evidence ?? [])
            {
                var at = row.Problem.Length == 0
                    ? -1
                    : rows.FindIndex(r => string.Equals(r.Problem, row.Problem, StringComparison.OrdinalIgnoreCase));
                if (at >= 0) rows[at] = row;
                else rows.Add(row);
            }

            return rows;
        }
    }

    /// <summary>
    /// The problem for the call that writes this tool's hook: the category's and the tool's own. Null
    /// when the operator stated neither.
    /// </summary>
    public string? HookGuidance(string? product) =>
        GccNicheFraming.HookFor(Category?.CoreProblem, product, Own?.CoreProblem);
}

/// <summary>
/// Reads <see cref="GccNicheFraming"/> out of a create's <c>BriefJson</c>.
/// </summary>
/// <remarks>
/// <para>
/// Wire shape, under <c>nicheFraming</c>:
/// <code>
/// {
///   "nicheFraming": {
///     "taxonomyPath": "Accounting &gt; Cash Flow Forecasting &gt; Accounts Receivable",
///     "coreProblem": "...",
///     "painPoints": "one failure per paragraph, blank line between",
///     "automationToPitch": "...",
///     "perTool": { "bill.com": { "coreProblem": "...", "painPoints": "...", "automationToPitch": "..." } }
///   }
/// }
/// </code>
/// </para>
/// <para>
/// <b>One category set, and a section for each tool.</b> Jeff's research states one problem for
/// every partner and then, per tool, a pain point, the failures with the tool's remedy for each, and
/// a pitch. A tool's section adds to the category's and replaces none of it
/// (<see cref="GccToolFraming"/>); a tool with nothing entered has the category's alone.
/// </para>
/// <para>
/// <b>Keyed by host, never by a typed name.</b> <c>GccPartnerToolSlices</c> buckets evidence by
/// <see cref="GccRequiredToolMentions.HostKeyOf"/> and names products through
/// <c>GccRequiredToolMentions.AnchorLookup</c>. A key of "Bill" would not match <c>bill.com</c>'s
/// slice, and the framing would then reach the wrong page or none at all — silently, because a null
/// <c>Guidance</c> is indistinguishable from no framing. The host is the one identity both sides agree
/// on.
/// </para>
/// <para>
/// Every failure is silent and returns null, per this codebase's no-exceptions rule: a brief that will
/// not parse means no framing, which the prompts already handle.
/// </para>
/// </remarks>
public static class GccNicheFramingReader
{
    private const string Root = "nicheFraming";

    /// <summary>The category-level framing, or null when the brief carries none.</summary>
    public static GccNicheFraming? ForCategory(string? briefJson)
    {
        var root = ReadRoot(briefJson);
        if (root is null) return null;

        var framing = ReadFraming(root.Value);
        return framing is not null && framing.HasAny ? framing : null;
    }

    /// <summary>
    /// The framing for a page about every partner, the pillar and the blog: the category's, and the
    /// word that its failures are the partners' when the operator entered none for the category.
    /// </summary>
    /// <param name="failuresAreUnderThePartners">
    /// <see cref="GccPartnerAnswers.CarryThePagesFailures"/> for this brief and this project's partners:
    /// the same answer the block the page is shown was worded by.
    /// </param>
    /// <remarks>
    /// A brief with nothing for the category and failures under its partners still gives a framing here,
    /// where <see cref="ForCategory"/> gives none: the failure sections have something to argue from.
    /// </remarks>
    public static GccNicheFraming? ForEveryPartner(string? briefJson, bool failuresAreUnderThePartners)
    {
        var category = ForCategory(briefJson);
        if (!failuresAreUnderThePartners || category is { PainPoints.Count: > 0 }) return category;
        return (category ?? new GccNicheFraming(string.Empty, [], string.Empty)) with { FailuresAreUnderThePartners = true };
    }

    /// <summary>
    /// The framing for one product: the category's, and what the operator entered in that product's
    /// own section. Null when neither exists.
    /// </summary>
    /// <param name="partnerUrls">
    /// The project's declared partner URLs, which is what resolves a product name back to the host its
    /// section is keyed by. Without them only the category framing can be found.
    /// </param>
    public static GccToolFraming? ForProduct(
        string? briefJson,
        IReadOnlyList<string>? partnerUrls,
        string? productName) =>
        ForHost(briefJson, HostForProduct(briefJson, partnerUrls, productName));

    /// <summary>
    /// The operator's FAQ for one tool: <c>perTool[host].faqQuestions</c>, each question with the
    /// answer entered for it, resolved through the same product-to-host lookup as
    /// <see cref="ForProduct"/>. The tool's own entry only -- the category has no FAQ for a tool page,
    /// so nothing merges. Empty when the product resolves to no host or the entry carries none.
    /// </summary>
    public static IReadOnlyList<GccFaqEntry> ToolFaq(
        string? briefJson,
        IReadOnlyList<string>? partnerUrls,
        string? productName) =>
        FaqForHost(briefJson, HostForProduct(briefJson, partnerUrls, productName));

    /// <summary>
    /// The operator's FAQ for one partner host (the <c>perTool</c> key, e.g. <c>bill.com</c>): each
    /// question once, in the order written, with its answer. Read by <see cref="GccFaqAnswers.Read"/>,
    /// which reads the pillar's and the blog's questions too.
    /// </summary>
    public static IReadOnlyList<GccFaqEntry> FaqForHost(string? briefJson, string? host)
    {
        foreach (var section in OwnSections(briefJson, host))
        {
            return TryGetPropertyIgnoreCase(section, "faqQuestions", out var faq) ? GccFaqAnswers.Read(faq) : [];
        }

        return [];
    }

    /// <summary>
    /// The framing for one partner host (the <c>perTool</c> key, e.g. <c>tipalti.com</c>): the
    /// category's, and that host's own section. Null when neither exists. Retrieval resolves partners
    /// by host, not by product name, so this is the entry it uses.
    /// </summary>
    public static GccToolFraming? ForHost(string? briefJson, string? host)
    {
        var framing = new GccToolFraming(ForCategory(briefJson), OwnForHost(briefJson, host));
        return framing.Category is null && framing.Own is null ? null : framing;
    }

    /// <summary>
    /// What the operator entered in one partner host's own section and nowhere else: its "Core pain
    /// point", its failures and rows, and its pitch, merged with nothing. Null when the brief holds
    /// none of them for the host.
    /// </summary>
    /// <remarks>
    /// The one reader of a tool's own section. A partner's search and its own page read it beside the
    /// category's (<see cref="ForHost"/>); a page about every partner reads it alone, to say which
    /// partner answers which failure (<see cref="GccPartnerAnswers"/>). Until 2026-10-12 there were
    /// three readers of that section, one for its rows, one for its two statements and one that
    /// merged it over the category's, and what one of them left out was a box entered and not used.
    /// <para>
    /// A section opened and left blank is not a section: the tool has the category's alone.
    /// </para>
    /// </remarks>
    public static GccNicheFraming? OwnForHost(string? briefJson, string? host)
    {
        foreach (var section in OwnSections(briefJson, host))
        {
            var own = ReadFraming(section);
            if (own is not null && own.HasAny) return own;
        }

        return null;
    }

    /// <summary>
    /// Each <c>perTool</c> entry filed under <paramref name="host"/>: by the host itself, or by an
    /// address on it. One, as the form saves a brief; in the order written when a brief written by
    /// hand holds more.
    /// </summary>
    private static IEnumerable<JsonElement> OwnSections(string? briefJson, string? host)
    {
        var wanted = (host ?? string.Empty).Trim();
        if (wanted.Length == 0) yield break;

        var root = ReadRoot(briefJson);
        if (root is null) yield break;

        if (!TryGetPropertyIgnoreCase(root.Value, "perTool", out var perTool)
            || perTool.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var entry in perTool.EnumerateObject())
        {
            if (!string.Equals(GccRequiredToolMentions.HostKeyOf(Absolute(entry.Name)), wanted, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(entry.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Value.ValueKind == JsonValueKind.Object) yield return entry.Value;
        }
    }

    /// <summary>
    /// The taxonomy path the operator researched, e.g.
    /// <c>["Accounting","Cash Flow Forecasting","Accounts Receivable"]</c>. Accepts a string split on
    /// <c>&gt;</c>, <c>-&gt;</c>, <c>›</c> or <c>→</c>, or an array.
    /// </summary>
    /// <remarks>
    /// Its first level is a department: <c>Accounting</c> is a member of <c>Departments.Slugs</c>, which
    /// is why the research template lines up with the one axis the URL scheme already has. The caller
    /// decides whether to trust it as a department — this only reads what was written.
    /// </remarks>
    public static IReadOnlyList<string> TaxonomyPath(string? briefJson)
    {
        var root = ReadRoot(briefJson);
        if (root is null) return [];

        if (!TryGetPropertyIgnoreCase(root.Value, "taxonomyPath", out var prop)) return [];

        var parts = new List<string>();
        if (prop.ValueKind == JsonValueKind.String)
        {
            var raw = prop.GetString() ?? string.Empty;
            foreach (var piece in raw.Replace("->", ">").Replace('›', '>').Replace('→', '>').Split('>'))
            {
                var trimmed = piece.Trim();
                if (trimmed.Length > 0) parts.Add(trimmed);
            }
        }
        else if (prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in prop.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var trimmed = (el.GetString() ?? string.Empty).Trim();
                if (trimmed.Length > 0) parts.Add(trimmed);
            }
        }

        return parts;
    }

    /// <summary>
    /// The operator's questions for the appointment: the closing asks the reader to book and to answer
    /// these when booking. Empty when the brief carries none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Category-level, with no per-tool override, and that is a property of the data rather than a
    /// simplification.</b> Jeff's set for AP approval workflows asks how many invoices need an approval,
    /// who approves spending, and whether the audit trail can answer "who approved this payment and
    /// why" — every question is about the reader's own process and none names a product. There is
    /// nothing for a partner to override. So this reads from the framing root only, like
    /// <see cref="TaxonomyPath"/>, and is deliberately not a field on <see cref="GccNicheFraming"/>,
    /// which <see cref="ForProduct"/> resolves per product.
    /// </para>
    /// <para>
    /// <b>One question per line — not the paragraph rule <see cref="ReadParagraphs"/> uses.</b> A failure
    /// mode is a paragraph, which is why pain points split on blank lines; a discovery question is a
    /// single line, and eight consecutive ones read through that rule collapse into one run-on entry.
    /// The two formats differ, so the two readers differ.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> DiagnosisQuestions(string? briefJson)
    {
        var root = ReadRoot(briefJson);
        return root is null ? [] : ReadQuestionLines(root.Value, "diagnosisQuestions");
    }

    /// <summary>
    /// The host whose section of the brief is <paramref name="productName"/>'s, or empty when the name
    /// matches no declared partner.
    /// </summary>
    internal static string HostForProduct(
        string? briefJson,
        IReadOnlyList<string>? partnerUrls,
        string? productName)
    {
        var wanted = (productName ?? string.Empty).Trim();
        if (wanted.Length == 0 || partnerUrls is null || partnerUrls.Count == 0) return string.Empty;

        // The same lookup the fan-out names its pages from, so the two cannot disagree about which
        // host a product is.
        var names = GccRequiredToolMentions.AnchorLookup(briefJson, partnerUrls);
        foreach (var pair in names)
        {
            if (string.Equals(pair.Value.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Key;
            }
        }

        // A product named as its host ("bill.com") rather than as its product name still resolves.
        var direct = GccRequiredToolMentions.HostKeyOf(Absolute(wanted));
        return names.ContainsKey(direct) ? direct : string.Empty;
    }

    /// <summary>A bare host becomes a URL so <c>HostKeyOf</c>, which requires an absolute URI, can read it.</summary>
    private static string Absolute(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"https://{trimmed}";
    }

    private static JsonElement? ReadRoot(string? briefJson)
    {
        if (string.IsNullOrWhiteSpace(briefJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(briefJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!TryGetPropertyIgnoreCase(doc.RootElement, Root, out var root)) return null;
            if (root.ValueKind != JsonValueKind.Object) return null;
            // Cloned because the JsonDocument is disposed on the way out of this method and an
            // undisposed element would read freed memory.
            return root.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GccNicheFraming? ReadFraming(JsonElement obj)
    {
        var core = ReadString(obj, "coreProblem");
        var automation = ReadString(obj, "automationToPitch");
        var pains = ReadParagraphs(obj, "painPoints");
        var evidence = ReadEvidence(obj);

        // Since 2026-10-08 the form enters each failure once, as a row, and derives painPoints from
        // the rows' Problem column on every change. A brief written by hand or by an older client
        // may carry a row whose failure no paragraph states; it is a failure the operator named all
        // the same, and is added after the paragraphs. Until 2026-10-12 that was done only when there
        // were no paragraphs at all, so beside even one paragraph a row's failure reached no writer.
        var stated = new List<string>(pains);
        foreach (var row in evidence)
        {
            if (row.Problem.Length == 0 || stated.Any(pain => IsRowsFailure(pain, row))) continue;
            stated.Add(row.Problem);
        }

        pains = stated;

        if (core.Length == 0 && automation.Length == 0 && pains.Count == 0 && evidence.Count == 0) return null;
        return new GccNicheFraming(core, pains, automation) { Evidence = evidence };
    }

    private static string ReadString(JsonElement obj, string name) =>
        TryGetPropertyIgnoreCase(obj, name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? (prop.GetString() ?? string.Empty).Trim()
            : string.Empty;

    /// <summary>
    /// The <c>evidence</c> array: objects with <c>problem</c>, <c>solution</c> and <c>terms</c>
    /// (an array of strings, or one comma-separated string -- the form stores an array; a pasted
    /// research answer may carry the string). A row with every part blank is not a row.
    /// </summary>
    private static IReadOnlyList<GccEvidenceRow> ReadEvidence(JsonElement obj)
    {
        if (!TryGetPropertyIgnoreCase(obj, "evidence", out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<GccEvidenceRow>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var problem = ReadString(item, "problem");
            var solution = ReadString(item, "solution");
            var terms = new List<string>();
            if (TryGetPropertyIgnoreCase(item, "terms", out var termsProp))
            {
                if (termsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in termsProp.EnumerateArray())
                    {
                        if (t.ValueKind == JsonValueKind.String) terms.Add((t.GetString() ?? string.Empty).Trim());
                    }
                }
                else if (termsProp.ValueKind == JsonValueKind.String)
                {
                    terms.AddRange((termsProp.GetString() ?? string.Empty).Split(',').Select(t => t.Trim()));
                }
            }
            terms.RemoveAll(string.IsNullOrWhiteSpace);
            if (problem.Length == 0 && solution.Length == 0 && terms.Count == 0) continue;
            rows.Add(new GccEvidenceRow(problem, solution, terms));
        }
        return rows;
    }

    /// <summary>
    /// One item per <b>line</b>, or an array. The counterpart to <see cref="ReadParagraphs"/>, for data
    /// whose unit is a line rather than a paragraph.
    /// </summary>
    /// <remarks>
    /// Blank lines separate nothing here, they are just skipped: an operator pasting a researched set
    /// often leaves one between questions, and reading that as a grouping would invent structure the
    /// closing has no use for.
    /// </remarks>
    private static IReadOnlyList<string> ReadQuestionLines(JsonElement obj, string name)
    {
        if (!TryGetPropertyIgnoreCase(obj, name, out var prop)) return [];

        var lines = new List<string>();
        if (prop.ValueKind == JsonValueKind.String)
        {
            var raw = (prop.GetString() ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in raw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) lines.Add(trimmed);
            }
        }
        else if (prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in prop.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var trimmed = (el.GetString() ?? string.Empty).Trim();
                if (trimmed.Length > 0) lines.Add(trimmed);
            }
        }

        return lines;
    }

    /// <summary>
    /// One item per <b>paragraph</b> — blank-line separated — or an array.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Paragraphs, not lines. Jeff, 2026-10-03: <i>"the data I am inputting is a paragraph"</i> — and it
    /// is: the research states each failure as a lead plus a paragraph explaining it, so splitting on
    /// every newline turned one failure into four fragments, each too thin for the writer to argue from.
    /// </para>
    /// <para>
    /// A single blank line is the separator, so a failure may run to as many sentences or wrapped lines
    /// as it needs. Text with no blank line in it is therefore <b>one</b> item, which is the correct
    /// reading of a single paragraph — and the one case where this differs from the old behaviour.
    /// </para>
    /// <para>
    /// The array shape is still accepted, for a programmatic writer (a later paste-and-extract
    /// acquisition) that already has the items separated.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> ReadParagraphs(JsonElement obj, string name)
    {
        if (!TryGetPropertyIgnoreCase(obj, name, out var prop)) return [];

        var lines = new List<string>();
        if (prop.ValueKind == JsonValueKind.String)
        {
            lines.AddRange(ParagraphsOf(prop.GetString()));
        }
        else if (prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in prop.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var trimmed = (el.GetString() ?? string.Empty).Trim();
                if (trimmed.Length > 0) lines.Add(trimmed);
            }
        }

        return lines;
    }

    /// <summary>
    /// Whether <paramref name="pain"/>, a saved failure, is the failure <paramref name="row"/> states:
    /// the row's failure as typed, or one of its paragraphs, or all of them as one. Without regard to case.
    /// </summary>
    /// <remarks>
    /// The form saves a row's failure twice, on the row and as a paragraph of the set's failures, and
    /// a failure typed with a blank line in it is one row and two paragraphs. Read by
    /// <see cref="GccNicheFraming.Failures"/>, so a failure is listed once with its row's remedy.
    /// </remarks>
    internal static bool IsRowsFailure(string pain, GccEvidenceRow row)
    {
        if (row.Problem.Length == 0) return false;
        if (string.Equals(pain, row.Problem, StringComparison.OrdinalIgnoreCase)) return true;
        var paragraphs = ParagraphsOf(row.Problem);
        return paragraphs.Contains(pain, StringComparer.OrdinalIgnoreCase)
            || string.Equals(pain, string.Join(" ", paragraphs), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A text's paragraphs: blank-line separated, the lines wrapped inside one joined into a sentence.
    /// </summary>
    /// <remarks>
    /// The one splitter. The saved failures are read with it (<see cref="ReadParagraphs"/>), and a
    /// row's failure is read with it to tell which saved paragraph is that row's
    /// (<see cref="IsRowsFailure"/>): two splitters would call one failure two.
    /// </remarks>
    internal static IReadOnlyList<string> ParagraphsOf(string? text)
    {
        var paragraphs = new List<string>();
        var raw = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var block in Regex.Split(raw, @"\n\s*\n"))
        {
            // Soft-wrapped lines inside one paragraph join into one sentence rather than staying
            // broken: the writer is given prose to argue from, not a column of fragments.
            var joined = string.Join(
                " ",
                block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var trimmed = joined.Trim();
            if (trimmed.Length > 0) paragraphs.Add(trimmed);
        }

        return paragraphs;
    }

    /// <summary>
    /// Case-insensitive property read. The frontend sends camelCase; nothing guarantees a future writer
    /// does, and a brief that silently reads as empty is the failure this whole file exists to remove.
    /// </summary>
    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value)) return true;

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

/// <summary>
/// One retrieval question from the brief's niche framing (the operator's <c>evidence</c> rows):
/// the reader's failure, the vendor's solution in the vendor's own vocabulary, and a few of the
/// vendor's terms. <paramref name="Solution"/> is sent to the index as <c>need</c> (the meaning
/// half); <paramref name="Terms"/>, joined, as <c>keyword</c> (the keyword half). Never quoted and
/// never shown to the writer as evidence: what retrieval returns for it is the evidence. Its failure
/// and its remedy are also shown to the writers as the operator's own note of what answers which
/// failure (<see cref="GccPartnerAnswers"/>): a partner's row to that partner's page, the pillar and
/// the blog, and a row saved for all partners to every one of them.
/// </summary>
public sealed record GccEvidenceRow(string Problem, string Solution, IReadOnlyList<string> Terms)
{
    /// <summary>The keyword half's text, or null when the row names no terms.</summary>
    public string? Keyword => Terms.Count == 0 ? null : string.Join(" ", Terms);

    /// <summary>What the meaning half is asked: the solution, else the problem.</summary>
    public string Need => Solution.Length > 0 ? Solution : Problem;
}
