using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// One check a draft failed: what it was, what to tell the writer, and whether it refuses the draft
/// or ships with it reported.
/// </summary>
/// <param name="Check">A stable name, so two verdicts can be compared check by check.</param>
/// <param name="Detail">The operator-facing reason, naming what was found.</param>
/// <param name="Refuses">True when a draft failing this is not saved. False when it is saved with
/// <paramref name="Detail"/> in its warnings -- a missing partner, an unlinked closing.</param>
/// <param name="RetryInstruction">What the single retry is told, so the writer fixes this and not
/// something else.</param>
public sealed record GccGuardFinding(string Check, string Detail, bool Refuses, string RetryInstruction);

/// <summary>Every check one draft failed. Empty is a draft that passes everything.</summary>
public sealed record GccGuardVerdict(IReadOnlyList<GccGuardFinding> Findings)
{
    public IReadOnlyList<GccGuardFinding> Refusals => [.. Findings.Where(f => f.Refuses)];

    public IReadOnlyList<GccGuardFinding> Gaps => [.. Findings.Where(f => !f.Refuses)];

    public bool Clean => Findings.Count == 0;

    /// <summary>The names of the checks this draft failed.</summary>
    public IReadOnlySet<string> FailedChecks =>
        Findings.Select(f => f.Check).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every finding's retry instruction, once each, for the single retry.</summary>
    public string RetryInstructions =>
        string.Join(
            Environment.NewLine,
            Findings.Select(f => f.RetryInstruction).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal));

    /// <summary>
    /// Whether <paramref name="retry"/> may replace <paramref name="draft"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A retry is taken only when the checks it fails are a strict subset of the checks the draft
    /// failed: it fixed at least one thing and broke nothing. So a retry can never fail a check the
    /// draft passed and still be taken: the CTA retry that fixed the link and dropped a partner, the
    /// mentions retry that came back with an unlicensed heading, and the tool retry that fixed the
    /// link and lost the quotation were each that shape, and each was stopped (or not) by a
    /// condition written beside that one retry. This is the condition, once.
    /// </para>
    /// <para>
    /// Not "fewer refusals": that let a retry swap one failure for a new one -- fix an unlicensed
    /// heading, add an invented figure -- and be kept, which is a different draft with a different
    /// fault, not a better one (review, 2026-10-04).
    /// </para>
    /// </remarks>
    public static bool RetryReplaces(GccGuardVerdict draft, GccGuardVerdict retry) =>
        retry.FailedChecks.IsProperSubsetOf(draft.FailedChecks);
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
    IReadOnlyList<string>? UnlistedTools = null);

/// <summary>
/// The guard for each long-form type: one function that runs every check, called on the first draft
/// and on the retry alike.
/// </summary>
/// <remarks>
/// <para>
/// The checks used to be run one at a time, each with its own retry and its own idea of what that
/// retry had to preserve. So a retry could pass fewer checks than the draft it replaced: the pillar's
/// mentions and CTA retries re-ran heading provenance but not the tools-section guard, rebuilt the
/// document from the retry's sections and dropped the People Also Ask section on the way. Running
/// all of them on every draft makes that impossible to write.
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
                Refuses: true,
                GccRequiredToolMentions.RetryInstruction(unnamed)));
        }

        // No candidates is the legacy path with no create and no partner evidence: there is nothing a
        // quotation could be checked against, so none is required. Candidates present but empty is a
        // grounded page with nothing quotable, and that is refused below.
        var quoteViolations = inputs.QuoteCandidates is null
            ? []
            : GccToolQuoteGuard.FindViolations(sections, inputs.QuoteCandidates);
        if (quoteViolations.Count > 0)
        {
            findings.Add(new GccGuardFinding(
                "quotation",
                "The tool page does not carry a verifiable block quotation. " + string.Join(" ", quoteViolations),
                Refuses: true,
                "The page must carry one block quotation: a quote paragraph whose \"candidate\" is the "
                + "number of one listed QUOTABLE SPAN. " + string.Join(" ", quoteViolations)));
        }

        var quotes = CountQuotes(document);
        if (quotes > 1)
        {
            findings.Add(new GccGuardFinding(
                "one-quotation",
                $"The tool page carries {quotes} block quotations; it carries exactly one.",
                Refuses: true,
                $"The last attempt carried {quotes} block quotations. Keep exactly one quote paragraph -- "
                + "the one that best shows how the product solves the problem -- and write the rest as prose."));
        }

        AddLinkFindings(document, inputs, findings);
        AddNumberFindings(document, inputs, findings);
        AddCurrencyFindings(document, inputs, findings);
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
                Refuses: true,
                GccToolsSectionGuard.RetryInstruction(toolsSections)));
        }

        if (inputs.Provenance is { } provenance)
        {
            // Checked even when a tools section was found: the two are separate failures, and a
            // retry that fixes one must not be taken for having fixed both.
            var unlicensed = GccHeadingProvenanceGuard.FindUnlicensedHeadings(headed, provenance);
            if (unlicensed.Count > 0)
            {
                findings.Add(new GccGuardFinding(
                    "heading-provenance",
                    $"The {type} body contains unlicensed headings: " + string.Join("; ", unlicensed),
                    Refuses: true,
                    GccHeadingProvenanceGuard.RetryInstruction(unlicensed, provenance)));
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
                Refuses: true,
                $"The last attempt carried {quotes} quote paragraph(s). A {type} never quotes: write every "
                + "quote paragraph as an ordinary text paragraph in your own words, attributing the claim to "
                + "its source with the URL as that run's \"href\"."));
        }

        AddLinkFindings(document, inputs, findings);
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
                Refuses: true,
                $"The last attempt named {named}. They are not this project's partners: remove every mention "
                + "of them, and any link to them. Name only the partner tools listed."));
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
                Refuses: false,
                GccRequiredToolMentions.RetryInstruction(missing)));
        }

        AddClosingFinding(document, inputs, findings);
        return new GccGuardVerdict(findings);
    }

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
            Refuses: true,
            $"The last attempt linked {listed}, which is not a page you were given. Remove those hrefs. A run "
            + "may link only the scheduler, the publisher's own pages, or a URL listed in QUOTEABLE RESEARCH; "
            + "a competitor is read and never linked. A tool is linked only at the exact path listed for it, "
            + "and a tool that is not listed is not linked."));
    }

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
            Refuses: true,
            $"The last attempt stated figures that are in none of the evidence you were given: {named}. A "
            + "number may appear only if it is in the evidence. Remove each one or replace it with the figure "
            + "the evidence actually gives; never estimate, round or convert."));
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
            Refuses: true,
            $"The last attempt stated money that is not in US dollars: {named}. State an amount of money only "
            + "in US dollars. Where the evidence gives a price only in another currency, do not state the "
            + "price at all -- never convert it, and never keep the number and drop the currency. Say the "
            + "vendor publishes its pricing, and leave the figure out."));
    }

    private static void AddClosingFinding(ContentDocument document, GccGuardInputs inputs, List<GccGuardFinding> into)
    {
        var violations = GccClosingCtaGuard.FindViolations(document, inputs.ConsultationHref);
        if (violations.Count == 0) return;

        into.Add(new GccGuardFinding(
            "closing-link",
            "The draft ships without a scheduler link. " + string.Join(" ", violations),
            Refuses: false,
            GccClosingCtaGuard.RetryInstruction(inputs.ConsultationHref!)));
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
