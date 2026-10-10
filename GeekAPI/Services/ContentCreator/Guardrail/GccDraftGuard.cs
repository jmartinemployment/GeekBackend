using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.Gcw;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// One check a draft failed: what it was, and whether it refuses the draft or ships with it reported.
/// </summary>
/// <param name="Check">A stable name, so two verdicts can be compared check by check.</param>
/// <param name="Detail">The operator-facing reason, naming what was found.</param>
/// <param name="Refuses">True when a draft failing this is not saved. False when it is saved with
/// <paramref name="Detail"/> in its warnings -- a missing partner, an unlinked closing.</param>
public sealed record GccGuardFinding(string Check, string Detail, bool Refuses);

/// <summary>Every check one draft failed. Empty is a draft that passes everything.</summary>
public sealed record GccGuardVerdict(IReadOnlyList<GccGuardFinding> Findings)
{
    public IReadOnlyList<GccGuardFinding> Refusals => [.. Findings.Where(f => f.Refuses)];

    public IReadOnlyList<GccGuardFinding> Gaps => [.. Findings.Where(f => !f.Refuses)];

    public bool Clean => Findings.Count == 0;

    /// <summary>The names of the checks this draft failed.</summary>
    public IReadOnlySet<string> FailedChecks =>
        Findings.Select(f => f.Check).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// What a draft is checked against: the evidence it was written from and the links it may carry.
/// </summary>
/// <param name="Provenance">The heading licences, for the types that tag headings. Null for Tool,
/// which is licensed by its outline and its quotation instead.</param>
/// <param name="RequiredTools">Partners the piece must name. Empty for Tool, which is about one.</param>
/// <param name="ConsultationHref">The scheduler anchor the closing must link, when the publisher has one.</param>
/// <param name="AllowedLinkUrls">Exact URLs a run may link: the retrieved partner evidence pages and
/// the publisher's own retrieved pages.</param>
/// <param name="PublisherHosts">Hosts the publisher owns. Any page on them may be linked -- the tool
/// pages, the blog, the site.</param>
/// <param name="NumberEvidence">The evidence block, the extraction, and the brief, topic and notes. Not
/// the prompt's instructions or outline: their numbers are targets ("600-850 words"), and a check that
/// licensed a figure because the prompt said it would license nothing.</param>
/// <param name="QuoteCandidates">Tool only: the numbered spans the quotation must come from.</param>
/// <param name="AppendedSections">How many sections at the end of the document were written outside the
/// body's outline -- the pillar's People Also Ask, the tool's FAQ. Their headings are the questions
/// themselves and carry no provenance tag, so they are held to every other check but not to the two
/// heading checks. Counted from the end rather than matched by reference, because ContentGuardrail
/// rebuilds every section it cleans.</param>
public sealed record GccGuardInputs(
    GccHeadingProvenanceEvidence? Provenance,
    IReadOnlyList<string> RequiredTools,
    string? ConsultationHref,
    IReadOnlySet<string> AllowedLinkUrls,
    IReadOnlySet<string> PublisherHosts,
    string NumberEvidence,
    IReadOnlyList<GccQuoteCandidate>? QuoteCandidates = null,
    int AppendedSections = 0,
    // Where the publisher's tool pages live ("/tools"), and the tool pages the writer was handed. A
    // link under the first must be one of the second: the project's declared partners, and no others.
    string? ToolBasePath = null,
    IReadOnlySet<string>? ToolPaths = null,
    // Tools the publisher's site lists that are not this project's partners. A pillar or blog that
    // names one is refused.
    IReadOnlyList<string>? UnlistedTools = null,
    // The keyword the page is scored on. Null is the legacy path with no keyword, and no keyword check.
    string? Keyword = null,
    // The words the page's SEO score holds its type to (GccLongFormTypes.GetSeoLengthRules). Null is a
    // caller that names no type, and no length check.
    int? PageFloorWords = null);

/// <summary>
/// The guard for each long-form type: one function that runs every check on the draft, once.
/// </summary>
/// <remarks>
/// <para>
/// Every check runs on every draft, so no check can be skipped by the path a draft took to get here.
/// A draft that fails one is refused or shipped with the gap reported; it is never sent back.
/// </para>
/// <para>
/// It also makes four promises the prompts already make true in code, where nothing checked them:
/// no quotation on a pillar or blog; exactly one on a tool page; no link to anywhere but the
/// publisher, the scheduler and the evidence the writer was given -- which is the only way "never cite
/// or link a competitor" holds, since competitor URLs are printed into the prompt; and no number the
/// evidence does not contain.
/// </para>
/// </remarks>
public static partial class GccDraftGuard
{
    /// <summary>The check a page under its type's word floor fails. Read by the writer to decide whether its calls' own shortfalls are listed.</summary>
    public const string PageLengthCheck = "page-length";

    public static GccGuardVerdict Pillar(ContentDocument document, GccGuardInputs inputs) =>
        LongForm(document, inputs, "pillar");

    public static GccGuardVerdict Blog(ContentDocument document, GccGuardInputs inputs) =>
        LongForm(document, inputs, "blog");

    public static GccGuardVerdict Tool(ContentDocument document, GccGuardInputs inputs)
    {
        var findings = new List<GccGuardFinding>();
        var sections = document.Sections;

        // The page's own subject has to appear in it. A tool page that never names its tool is not a
        // thin page, it is a category explainer wearing a product's title -- refused, where a pillar
        // missing one of five partners ships with the gap named.
        var unnamed = GccRequiredToolMentions.Missing(document, inputs.RequiredTools);
        if (unnamed.Count > 0)
        {
            findings.Add(new GccGuardFinding(
                "names-product",
                $"The tool page never names {string.Join(", ", unnamed)}. A page about a product must name the product.",
                Refuses: true));
        }

        // No candidates is the legacy path with no create and no partner evidence: there is nothing a
        // quotation could be checked against, so none is required. With candidates, a quotation
        // that is present is held to them -- invented or misattributed words refuse the page -- but a
        // page with none ships and says so (Jeff, 2026-10-08: do not fail the page when no fitting
        // quotation was found). The gap is reported with the draft; nothing is substituted for it.
        if (inputs.QuoteCandidates is not null)
        {
            var quoteViolations = GccToolQuoteGuard.FindViolations(sections, inputs.QuoteCandidates);
            if (quoteViolations.Count > 0)
            {
                findings.Add(new GccGuardFinding(
                    "quotation",
                    "The tool page does not carry a verifiable block quotation. " + string.Join(" ", quoteViolations),
                    Refuses: true));
            }

            var missing = GccToolQuoteGuard.MissingQuotation(sections, inputs.QuoteCandidates);
            if (missing is not null)
            {
                findings.Add(new GccGuardFinding(
                    "blockquote-missing",
                    missing + " The page ships without one; the writer found no listed span that answers the problem.",
                    Refuses: false));
            }
        }

        var quotes = CountQuotes(document);
        if (quotes > 1)
        {
            findings.Add(new GccGuardFinding(
                "one-quotation",
                $"The tool page carries {quotes} block quotations; it carries exactly one.",
                Refuses: true));
        }

        AddLinkFindings(document, inputs, findings);
        AddLinkTextFindings(document, findings);
        AddOpeningLinkFindings(document, findings);
        AddNumberFindings(document, inputs, findings);
        AddCurrencyFindings(document, inputs, findings);
        AddLengthFinding(document, inputs, findings, "tool page");
        AddKeywordFindings(document, inputs, findings, "tool page");
        AddClosingFinding(document, inputs, findings);
        return new GccGuardVerdict(findings);
    }

    private static GccGuardVerdict LongForm(ContentDocument document, GccGuardInputs inputs, string type)
    {
        var findings = new List<GccGuardFinding>();
        var headed = document.Sections
            .Take(Math.Max(0, document.Sections.Count - inputs.AppendedSections))
            .ToList();

        var toolsSections = GccToolsSectionGuard.FindToolsSections(headed);
        if (toolsSections.Count > 0)
        {
            findings.Add(new GccGuardFinding(
                "tools-section",
                $"The {type} carries a section whose job is to list tools — "
                + string.Join(", ", toolsSections.Select(h => $"\"{h}\""))
                + ". Tools belong in the prose of the sections they serve.",
                Refuses: true));
        }

        if (inputs.Provenance is { } provenance)
        {
            // Checked even when a tools section was found: the two are separate failures.
            var unlicensed = GccHeadingProvenanceGuard.FindUnlicensedHeadings(headed, provenance);
            if (unlicensed.Count > 0)
            {
                findings.Add(new GccGuardFinding(
                    "heading-provenance",
                    $"The {type} body contains unlicensed headings: " + string.Join("; ", unlicensed),
                    Refuses: true));
            }
        }

        // The prompt says "Do not quote. No blockquotes" for these two types. The parser accepts a
        // quote paragraph from any type and no quote guard runs on them, so a pillar could ship a
        // quotation nobody verified under a cite nobody checked.
        var quotes = CountQuotes(document);
        if (quotes > 0)
        {
            findings.Add(new GccGuardFinding(
                "no-quotation",
                $"The {type} carries {quotes} block quotation(s). A {type} does not quote; only a tool page "
                + "carries a quotation, and only one checked against the partner's own words.",
                Refuses: true));
        }

        AddLinkFindings(document, inputs, findings);
        AddLinkTextFindings(document, findings);
        AddOpeningLinkFindings(document, findings);
        AddNumberFindings(document, inputs, findings);
        AddCurrencyFindings(document, inputs, findings);

        // "Tools like ApprovalMax, Melio, and Ramp" on a project whose partners do not include Melio
        // (Jeff, 2026-10-05). The publisher's site lists more tools than a project has partners, and
        // the writer reads that site.
        var unlisted = GccRequiredToolMentions.Named(document, inputs.UnlistedTools ?? []);
        if (unlisted.Count > 0)
        {
            var named = string.Join(", ", unlisted);
            findings.Add(new GccGuardFinding(
                "unlisted-tools",
                $"{Capitalized(type)} names {named}, which this project does not list as a partner. A {type} "
                + "names the project's partner tools and no other.",
                Refuses: true));
        }

        var missing = GccRequiredToolMentions.Missing(document, inputs.RequiredTools);
        if (missing.Count > 0)
        {
            findings.Add(new GccGuardFinding(
                "partner-mentions",
                $"{Capitalized(type)} names {inputs.RequiredTools.Count - missing.Count} of {inputs.RequiredTools.Count} "
                + $"partner tools. Missing: {string.Join(", ", missing)}. The draft is saved as written. Add the "
                + "missing partner where it belongs, or check that it has an indexed crawl, since a partner with "
                + "no evidence gives the writer nothing to say about it.",
                Refuses: false));
        }

        AddLengthFinding(document, inputs, findings, type);
        AddKeywordFindings(document, inputs, findings, type);
        AddClosingFinding(document, inputs, findings);
        return new GccGuardVerdict(findings);
    }

    /// <summary>
    /// The page's length, judged once and by the page's own score: the finished page's words, counted
    /// as the SEO report counts them (<see cref="GcwSeoAnalyzer.CountWords"/>), against the floor the
    /// report holds its type to. Under it the page ships and says so. A reported gap, never a refusal.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-10 length was reported a call at a time and never for the page. The run of that
    /// day wrote seven pages of 2,633 to 4,436 words, every one over its floor, and listed 17 calls
    /// "under a 600-word floor" among its 29 gaps: a page's five calls owe 600 each so that they add
    /// up to 3,000, and a page whose calls wrote 512, 660, 581, 689 and 662 has met it. A call's own
    /// shortfall is now listed only beside this line, where it says which call left the page short.
    /// </remarks>
    private static void AddLengthFinding(ContentDocument document, GccGuardInputs inputs, List<GccGuardFinding> into, string type)
    {
        if (inputs.PageFloorWords is not { } floor || floor <= 0) return;
        var words = GcwSeoAnalyzer.CountWords(JsonSerializer.Serialize(document, GccDocumentJson.Options));
        if (words >= floor) return;

        into.Add(new GccGuardFinding(
            PageLengthCheck,
            $"The {type} is {words:N0} words. Its SEO score needs {floor:N0}.",
            Refuses: false));
    }

    /// <summary>
    /// The page's keyword, judged once and by the page's own score: the exact phrase as a share of the
    /// page's words, inside the band the SEO report passes (<see cref="GcwSeoAnalyzer"/>, the same
    /// reader, tokenizer and match). Outside it the page ships and says so, naming the sections that
    /// never use the phrase. A reported gap, never a refusal: the writer was asked for no count (Jeff,
    /// 2026-10-10), and what could go back without breaking the grammar, <see cref="GccKeywordRemap"/>
    /// has put back.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-10 the count was checked a call at a time against the call's share of a count
    /// sized to the page's floor. Nine of the eighteen warnings on 2026-10-07 landed on pages the
    /// score passed. The score's mark is a share of the words the page ends up with, closing and
    /// questions included, and cannot be divided among calls in advance the way a word floor can.
    /// </remarks>
    private static void AddKeywordFindings(ContentDocument document, GccGuardInputs inputs, List<GccGuardFinding> into, string type)
    {
        if (string.IsNullOrWhiteSpace(inputs.Keyword)) return;
        var phrase = inputs.Keyword.Trim();
        var counted = GcwSeoAnalyzer.CountKeyword(JsonSerializer.Serialize(document, GccDocumentJson.Options), phrase);
        if (counted.Words == 0) return;

        var used = $"The {type} uses \"{phrase}\" {counted.Uses} time{(counted.Uses == 1 ? string.Empty : "s")} in "
            + $"{counted.Words:N0} words ({counted.DensityPercent:0.00}%).";
        if (counted.DensityPercent < GcwSeoAnalyzer.MinKeywordDensityPercent)
        {
            // Each section counted as the score counts it, headings and subsections included.
            var never = new List<Section> { document.Lede }
                .Concat(document.Sections)
                .Where(s => !string.IsNullOrWhiteSpace(s.Heading) && UsesIn(s, phrase) == 0)
                .Select(s => $"\"{s.Heading}\"")
                .ToList();
            into.Add(new GccGuardFinding(
                "keyword-density",
                $"{used} Its SEO score needs {GcwSeoAnalyzer.MinKeywordDensityPercent:0.0}%."
                + (never.Count == 0 ? string.Empty : $" Sections that never use it: {string.Join(", ", never)}."),
                Refuses: false));
        }
        else if (counted.DensityPercent > GcwSeoAnalyzer.MaxKeywordDensityPercent)
        {
            into.Add(new GccGuardFinding(
                "keyword-density",
                $"{used} Its SEO score allows {GcwSeoAnalyzer.MaxKeywordDensityPercent:0.0}%; it reads stuffed.",
                Refuses: false));
        }
    }

    private static int UsesIn(Section section, string phrase) =>
        GcwSeoAnalyzer.CountKeyword(
            JsonSerializer.Serialize(
                new ContentDocument(new Section("h2", string.Empty, [], null, []), [section]),
                GccDocumentJson.Options),
            phrase).Uses;

    /// <summary>
    /// Every run and section href must lead somewhere the writer was given: the scheduler, the
    /// publisher's own site, or a page of the evidence.
    /// </summary>
    private static void AddLinkFindings(ContentDocument document, GccGuardInputs inputs, List<GccGuardFinding> into)
    {
        var refused = new List<string>();
        foreach (var href in Hrefs(document))
        {
            if (!LinkAllowed(href, inputs)) refused.Add(href);
        }

        if (refused.Count == 0) return;

        var listed = string.Join(", ", refused.Distinct(StringComparer.OrdinalIgnoreCase));
        into.Add(new GccGuardFinding(
            "links",
            $"The draft links outside the evidence it was given: {listed}. A link may go to the scheduler, "
            + "the publisher's own pages, or a partner page in QUOTEABLE RESEARCH -- never a competitor and "
            + "never an address the writer supplied itself. A tool page is linked only at the path listed "
            + "for one of this project's partner tools.",
            Refuses: true));
    }

    /// <summary>
    /// The most words a link may sit on. A link is the name of what it leads to -- a product, a page,
    /// a source -- and the longest of those is a page title; a sentence is not a name.
    /// </summary>
    /// <remarks>
    /// The writer is told nothing about links and cannot write one (Jeff, 2026-10-10); the links on a
    /// page are the closing's and <c>GccToolLinker</c>'s, each on a name. This check holds code to the
    /// limit, not the model.
    /// </remarks>
    public const int MaxLinkWords = 12;

    /// <summary>
    /// A link sits on a few words, never on a passage.
    /// </summary>
    /// <remarks>
    /// The Stampli tool page of 2026-10-05: two sections were 85% and 67% link text, each paragraph one
    /// run of 280 to 650 characters carrying the URL of the page it paraphrased (Jeff: "this is
    /// ridiculous, 90% of the section is a link or anchor"). The writer had been told to attribute a
    /// paraphrase "with the page it comes from as that run's href", and a paragraph is one run. Where
    /// the link leads was checked; what it sat on was not.
    /// </remarks>
    private static void AddLinkTextFindings(ContentDocument document, List<GccGuardFinding> into)
    {
        var passages = AllSections(document)
            .SelectMany(section => section.Paragraphs)
            .SelectMany(Runs)
            .Where(run => !string.IsNullOrWhiteSpace(run.Href) && WordCount(run.Text) > MaxLinkWords)
            .ToList();
        if (passages.Count == 0) return;

        var named = string.Join("; ", passages.Select(run =>
            $"\"{Opening(run.Text)}...\" ({WordCount(run.Text)} words, linked to {run.Href!.Trim()})"));
        into.Add(new GccGuardFinding(
            "link-text",
            $"The draft puts a link on {passages.Count} whole passage(s) instead of on a few words: {named}. "
            + $"A link sits on the name of what it leads to, {MaxLinkWords} words at most.",
            Refuses: true));
    }

    /// <summary>
    /// The opening carries no links. Reported with the draft, not refused: the page is saved as written and the
    /// operator is told which words in the opening were linked.
    /// </summary>
    /// <remarks>
    /// The opening prompts said "a link in the opening goes only to a page whose address is printed in this
    /// prompt", which permits one, and in the 2026-10-07 run the Blog's opening linked the publisher's home page
    /// and the Bill, Ramp and Stampli openings linked the vendors' own sites, while the Pillar's linked nothing.
    /// The prompts now say the opening carries no links; this is what makes that a check and not a request.
    /// <para>
    /// 2026-10-09: tried flipping this to <c>Refuses: true</c>, since a tool/citation link in the opening and a
    /// CTA-style link (the failure mode above) were being treated alike and Jeff's preference is "without" for
    /// both anyway. Reverted: shared test fixtures carry an opening linking <c>#consultationAppointment2xl</c>,
    /// a same-page anchor to the page's own closing scheduler section, not an external link of any kind. This
    /// check cannot yet tell a same-page anchor, an external vendor/tool citation link, and an actual CTA
    /// mislink apart -- refusing all three alike broke 15 unrelated tests. Before refusing here, that
    /// distinction has to exist first; see <c>opening-links-rule-is-preference-not-correctness</c> in project
    /// memory for the full incident. Jeff's current workaround: read this finding's text and manually remove
    /// the link before publishing.
    /// </para>
    /// </remarks>
    private static void AddOpeningLinkFindings(ContentDocument document, List<GccGuardFinding> into)
    {
        var linked = OpeningSections(document.Lede)
            .SelectMany(section => section.Paragraphs)
            .SelectMany(Runs)
            .Where(run => !string.IsNullOrWhiteSpace(run.Href))
            .ToList();
        if (linked.Count == 0) return;

        var named = string.Join("; ", linked.Select(run => $"\"{Opening(run.Text)}\" ({run.Href!.Trim()})"));
        into.Add(new GccGuardFinding(
            "opening-links",
            $"The opening links {linked.Count} place(s): {named}. The opening carries no links. The draft is saved as written.",
            Refuses: false));
    }

    private static IEnumerable<Section> OpeningSections(Section section) =>
        new[] { section }.Concat(section.Children.SelectMany(OpeningSections));

    /// <summary>How many words a run is. One definition for the link-text check.</summary>
    internal static int WordCount(string? text) =>
        (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>The first few words of a run, enough to find it in the draft.</summary>
    internal static string Opening(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(8));

    internal static bool LinkAllowed(string href, GccGuardInputs inputs)
    {
        var trimmed = href.Trim();
        if (trimmed.Length == 0) return true;

        if (!string.IsNullOrWhiteSpace(inputs.ConsultationHref)
            && string.Equals(trimmed, inputs.ConsultationHref.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (inputs.AllowedLinkUrls.Contains(trimmed)) return true;

        // A root-relative path is a page on the publisher's own site by construction. Read by host
        // alone, every one of them was refused (2026-10-04). "//host/..." is not a path: it is an
        // absolute address with the scheme left off, and goes through the host check below.
        var relative = trimmed.StartsWith('/') && !trimmed.StartsWith("//", StringComparison.Ordinal);
        var host = relative ? string.Empty : GccRequiredToolMentions.HostKeyOf(trimmed);
        var onPublisherSite = relative || (host.Length > 0 && inputs.PublisherHosts.Contains(host));
        if (!onPublisherSite) return false;

        // Among the publisher's pages, a tool page is one the writer was handed -- a declared partner's
        // -- or it is not linked. A pillar linked /tools/marketing/melio and /tools/marketing/plooto on
        // a project that lists neither (Jeff, 2026-10-05), at paths no page is published under.
        var path = GccContentPath.PathOf(trimmed);
        var toolBase = inputs.ToolBasePath;
        var isToolPage = !string.IsNullOrWhiteSpace(toolBase)
            && path.StartsWith(toolBase + "/", StringComparison.OrdinalIgnoreCase);
        return !isToolPage || (inputs.ToolPaths?.Contains(path) ?? false);
    }

    /// <summary>
    /// A figure in the draft must be one the writer was shown. What a figure is -- and is not -- is
    /// <see cref="GccFigureGrammar"/>; the evidence is the evidence block, the extraction, and the
    /// brief, topic and notes (<see cref="GccGuardInputs.NumberEvidence"/>).
    /// </summary>
    private static void AddNumberFindings(ContentDocument document, GccGuardInputs inputs, List<GccGuardFinding> into)
    {
        var known = GccFigureGrammar.NumbersIn(inputs.NumberEvidence);
        var unsupported = new List<(string Number, string Sentence)>();
        foreach (var (text, cites) in Paragraphs(document))
        {
            foreach (var figure in GccFigureGrammar.Find(text, cites))
            {
                if (known.Contains(figure.Value)) continue;
                unsupported.Add((figure.Written, SentenceAround(text, figure.Index)));
            }
        }

        if (unsupported.Count == 0) return;

        var named = string.Join(
            "; ",
            unsupported.DistinctBy(u => u.Sentence).Take(6).Select(u => $"{u.Number} in \"{u.Sentence}\""));
        into.Add(new GccGuardFinding(
            "numbers",
            $"The draft states figures that appear in none of its evidence: {named}.",
            Refuses: true));
    }

    /// <summary>
    /// Money is in US dollars or it is not on the page (<see cref="GccCurrencyGrammar"/>). Refused: an
    /// amount written in another currency; and a dollar amount the evidence gives only in another
    /// currency -- the foreign price with its currency dropped, which reads as US dollars and is not.
    /// </summary>
    /// <remarks>
    /// A block quotation is exempt (Jeff, 2026-10-05: "Use of other currencies is acceptable in
    /// Blockquotes"). It is the partner's own published sentence, reproduced exactly; changing or
    /// refusing its currency would be editing a quotation.
    /// </remarks>
    private static void AddCurrencyFindings(ContentDocument document, GccGuardInputs inputs, List<GccGuardFinding> into)
    {
        var evidence = GccCurrencyGrammar.Find(inputs.NumberEvidence);
        var inDollars = evidence.Where(m => m.IsUsd || m.IsBareDollar).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        var foreignOnly = evidence
            .Where(m => m.IsForeign && !inDollars.Contains(m.Value))
            .GroupBy(m => m.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Currency, StringComparer.Ordinal);

        var wrong = new List<string>();
        foreach (var (text, _) in Paragraphs(document, includeQuotations: false))
        {
            foreach (var money in GccCurrencyGrammar.Find(text))
            {
                if (money.IsForeign)
                {
                    wrong.Add($"{money.Written} ({money.Currency}) in \"{SentenceAround(text, money.Index)}\"");
                }
                else if (foreignOnly.TryGetValue(money.Value, out var currency))
                {
                    wrong.Add(
                        $"{money.Written} in \"{SentenceAround(text, money.Index)}\" -- the evidence gives that "
                        + $"amount in {currency}, not US dollars");
                }
            }
        }

        if (wrong.Count == 0) return;

        var named = string.Join("; ", wrong.Distinct(StringComparer.Ordinal).Take(6));
        into.Add(new GccGuardFinding(
            "currency",
            $"The draft states money that is not in US dollars: {named}. Every amount is in US dollars.",
            Refuses: true));
    }

    private static void AddClosingFinding(ContentDocument document, GccGuardInputs inputs, List<GccGuardFinding> into)
    {
        AddQuizFinding(document, inputs.ConsultationHref, into);

        var violations = GccClosingCtaGuard.FindViolations(document, inputs.ConsultationHref);
        if (violations.Count == 0) return;

        into.Add(new GccGuardFinding(
            "closing-link",
            "The draft ships without a scheduler link. " + string.Join(" ", violations),
            Refuses: false));
    }

    /// <summary>
    /// The phrasings that turn the publisher's questions into a quiz the reader grades before booking.
    /// The instruction names them as forbidden; a page that uses one anyway is refused rather than trusted.
    /// </summary>
    private static readonly string[] QuizPhrasings =
    [
        "ask yourself",
        "asking yourself",
        "if these questions",
        "if any of these",
        "if your answers",
        "if the answers",
    ];

    /// <summary>
    /// The publisher's questions are answered when the reader books. They are not a self-check the
    /// appointment depends on (Jeff, 2026-10-05: "It was meant answer these questions when booking your
    /// appointment"; 2026-10-06: "Questions are not being used as intended").
    /// </summary>
    /// <remarks>
    /// The closing instruction has said so since 2026-10-05, and said which phrasings are forbidden.
    /// An instruction is not a check: a page that still wrote "consider asking yourself ... if these
    /// questions highlight inefficiencies, it may be time to book" would have shipped. The phrasings
    /// are matched anywhere on the page, since a quiz is a quiz wherever it sits.
    /// </remarks>
    private static void AddQuizFinding(ContentDocument document, string? anchorHref, List<GccGuardFinding> into)
    {
        // Read without the page's own closing. That line and the list under it are the operator's words,
        // built by code (GccClosing), not a quiz the writer framed: a question he wrote that happens to
        // contain "if your answers" must not refuse the page it is on.
        var (withoutClosing, _, _) = GccClosing.Detach(document.Sections, anchorHref);
        var text = GeekAPI.Services.Workflow.Services.ContentDocumentText.Flatten(document with { Sections = withoutClosing });
        var used = QuizPhrasings
            .Where(p => text.Contains(p, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (used.Count == 0) return;

        var listed = string.Join("\", \"", used);
        into.Add(new GccGuardFinding(
            "questions-quiz",
            $"The draft turns the publisher's questions into a quiz (\"{listed}\"). The reader answers them "
            + "when booking; the appointment does not depend on their answers.",
            Refuses: true));
    }

    private static int CountQuotes(ContentDocument document) =>
        AllSections(document).Sum(s => s.Paragraphs.Count(p => p is QuoteParagraph));

    private static IEnumerable<Section> AllSections(ContentDocument document)
    {
        var stack = new Stack<Section>();
        foreach (var section in document.Sections.Reverse()) stack.Push(section);
        stack.Push(document.Lede);
        while (stack.Count > 0)
        {
            var section = stack.Pop();
            yield return section;
            foreach (var child in section.Children.Reverse()) stack.Push(child);
        }
    }

    private static IEnumerable<Run> Runs(Paragraph paragraph) => paragraph switch
    {
        TextParagraph t => t.Runs,
        QuoteParagraph q => q.Runs,
        ListParagraph l => l.Items.SelectMany(i => i),
        _ => [],
    };

    private static IEnumerable<string> Hrefs(ContentDocument document)
    {
        foreach (var section in AllSections(document))
        {
            if (!string.IsNullOrWhiteSpace(section.Href)) yield return section.Href;
            foreach (var run in section.Paragraphs.SelectMany(Runs))
            {
                if (!string.IsNullOrWhiteSpace(run.Href)) yield return run.Href;
            }
        }
    }

    /// <summary>
    /// Paragraph text, one string per paragraph or list item so a sentence is never split, with the
    /// ranges of runs that carry a link. Headings are not here: a heading is not a claim (see
    /// <see cref="GccFigureGrammar"/>).
    /// </summary>
    /// <param name="includeQuotations">False leaves block quotations out: they are someone else's
    /// published words, reproduced as written.</param>
    private static IEnumerable<(string Text, IReadOnlyList<(int Start, int End)> Cites)> Paragraphs(
        ContentDocument document, bool includeQuotations = true)
    {
        static (string, IReadOnlyList<(int, int)>) Join(IEnumerable<Run> runs)
        {
            var text = new System.Text.StringBuilder();
            var cites = new List<(int, int)>();
            foreach (var run in runs)
            {
                var start = text.Length;
                text.Append(run.Text);
                if (!string.IsNullOrWhiteSpace(run.Href)) cites.Add((start, text.Length));
            }

            return (text.ToString(), cites);
        }

        foreach (var section in AllSections(document))
        {
            foreach (var paragraph in section.Paragraphs)
            {
                if (paragraph is ListParagraph list)
                {
                    foreach (var item in list.Items) yield return Join(item);
                    continue;
                }

                if (paragraph is QuoteParagraph && !includeQuotations) continue;

                var joined = Join(Runs(paragraph));
                if (joined.Item1.Length > 0) yield return joined;
            }
        }
    }

    private static string SentenceAround(string text, int index)
    {
        var start = text.LastIndexOfAny(['.', '!', '?'], Math.Max(0, index - 1));
        start = start < 0 ? 0 : start + 1;
        var end = text.IndexOfAny(['.', '!', '?'], index);
        end = end < 0 ? text.Length : end + 1;
        var sentence = text[start..end].Trim();
        return sentence.Length > 160 ? sentence[..160] + "…" : sentence;
    }

    private static string Capitalized(string type) => char.ToUpperInvariant(type[0]) + type[1..];

}
