using System.Globalization;
using System.Text;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>A citation code placed after a sentence that states a figure.</summary>
/// <param name="Heading">The heading of the section the sentence is under.</param>
/// <param name="Sentence">The sentence as the writer wrote it, before the citation was added.</param>
/// <param name="Figures">The figures in it, as written.</param>
/// <param name="Url">The research page the figures are on, which the citation links.</param>
/// <param name="Name">What the citation calls that page: the partner, the publisher, or the host.</param>
/// <param name="AsOf">When that page was crawled, or null when the research does not say.</param>
/// <param name="AlsoOn">How many other research pages state the same figures.</param>
public sealed record GccPlacedCitation(
    string Heading,
    string Sentence,
    IReadOnlyList<string> Figures,
    string Url,
    string Name,
    DateTimeOffset? AsOf,
    int AlsoOn);

/// <summary>A sentence that states a figure and could be cited to no page, and why.</summary>
public sealed record GccUncitedFigures(string Heading, string Sentence, IReadOnlyList<string> Figures, string Why);

/// <summary>
/// Every figure in the opening and the body is cited to the research page it is on: the source's name
/// linked to that page, and the date the page was crawled, placed by code after the sentence.
/// </summary>
/// <remarks>
/// <para>
/// Jeff's <c>ai-seo</c>, <c>seo-content</c>, <c>geo-citability</c> and <c>seo-geo</c> skills ask for
/// three things on a statistic: a named source, a link to it, and a date ("Cite sources: add
/// authoritative references with links", "Add dates to all statistics", "'According to [Source]'
/// framing"). geekatyourspot's GEO audit of 2026-10-20 (H3) counted 0 pillars in 23 linking an outside
/// source. The writer was asked to name a source in words and nothing checked it; no link and no date
/// were ever written.
/// </para>
/// <para>
/// <b>The writer still writes no link</b> (Jeff, 2026-10-10). Inline citations by the writer were tried
/// four ways between 2026-10-03 and 2026-10-09 and each failed a real run: a typed "[Source: title](url)",
/// an href on whole paragraphs, a copied anchor list, a page id marker. So, like <see cref="GccToolLinker"/>,
/// code does it: a sentence that states a figure gets " (Name, as of Oct 13, 2026)" before its full
/// stop, the name linked to the page the figure is on. A figure is what <see cref="GccFigureGrammar"/>
/// says it is, and a page "states" a figure when the grammar finds the same value in that page's title,
/// headings and passages, which is the same reading the <c>numbers</c> check makes of the evidence.
/// </para>
/// <para>
/// <b>Which page.</b> The pages that state every figure of the sentence, partner pages first in the
/// order the research lists them, then the publisher's own. A sentence that names a partner is cited to
/// that partner's page, and to nothing else: a figure the named partner's pages do not state is left
/// uncited and named for the operator, because citing another partner's page under that sentence would
/// attribute the figure to the wrong source. Otherwise the first page that states it all; how many
/// others do is on the run's record.
/// </para>
/// <para>
/// <b>Once per paragraph per page.</b> A paragraph that takes three figures from one page cites it on
/// the first sentence that needs it and not again. A list is one paragraph for this.
/// </para>
/// <para>
/// <b>Left uncited, and told to the operator, never a refusal:</b> a figure only the operator typed
/// (the Brief, the topic, the notes), which has no page; a figure only a competitor's page states, which
/// is never cited; a sentence whose figures are on different pages; and a sentence a partner is named in
/// whose pages do not state its figure. The <c>numbers</c> check still decides whether such a figure may
/// stay on the page at all; this only decides what it is cited to.
/// </para>
/// <para>
/// <b>What the grammar makes of the citation.</b> Its year sits inside parentheses and after a link, and
/// <see cref="GccFigureGrammar"/> reads a year there as attribution, not as a figure. The day is one or two
/// digits with no unit after it. So a citation adds no figure for the check to license.
/// </para>
/// <para>
/// Headings, block quotations, code and definitions are not read: a heading is not a claim, a quotation
/// carries its own <c>cite</c>, and the others are not prose. Runs are split, never rewritten; the
/// paragraph's text apart from the citation is unchanged.
/// </para>
/// </remarks>
public static class GccFigureSources
{
    /// <summary>What a page's figures may be cited to, and what they may not.</summary>
    /// <param name="PartnerPages">The partners' pages the writer was shown, in the research's order.</param>
    /// <param name="SitePages">The publisher's own pages the writer was shown.</param>
    /// <param name="CompetitorPages">The competitors' pages the writer was shown. Read, never cited.</param>
    /// <param name="PartnerNamesByHost">Each partner's product name by its host key (<see cref="GccRequiredToolMentions.HostKeyOf"/>).</param>
    /// <param name="PublisherName">What a citation to one of the publisher's own pages is called.</param>
    /// <param name="PublisherHosts">The hosts the publisher owns, by host key.</param>
    /// <param name="OperatorsOwnWords">The Brief, the topic and the notes as text: a figure found only there is the operator's.</param>
    public sealed record Inputs(
        IReadOnlyList<GccQuoteablePage> PartnerPages,
        IReadOnlyList<GccQuoteablePage> SitePages,
        IReadOnlyList<GccQuoteablePage> CompetitorPages,
        IReadOnlyDictionary<string, string> PartnerNamesByHost,
        string PublisherName,
        IReadOnlySet<string> PublisherHosts,
        string? OperatorsOwnWords);

    /// <summary>The opening and the body with their citations, and what was placed and what could not be.</summary>
    public sealed record Result(
        Section Lede,
        IReadOnlyList<Section> Sections,
        IReadOnlyList<GccPlacedCitation> Placed,
        IReadOnlyList<GccUncitedFigures> Uncited);

    public const string FromTheBrief = "the figure is the operator's own, from the Brief, and has no page to cite";
    public const string OnACompetitorsPage = "the figure is on a competitor's page, which is never cited";
    public const string OnDifferentPages = "its figures are on different pages, and no one page states them all";
    public const string OnNoPage = "the figure is in what the writer was shown but on no page that can be cited";

    /// <summary>A research page as a source: what it is called, when it was crawled, and the figures it states.</summary>
    private sealed record Source(string Url, string Name, DateTimeOffset? AsOf, IReadOnlySet<string> Numbers, string PartnerHost);

    private sealed record Choice(Source? Source, int AlsoOn, string? Why);

    private sealed class State(
        IReadOnlyList<Source> sources,
        IReadOnlySet<string> competitor,
        IReadOnlySet<string> operators,
        IReadOnlyList<(string Host, string Name)> partners)
    {
        public IReadOnlyList<Source> Sources { get; } = sources;
        public IReadOnlySet<string> Competitor { get; } = competitor;
        public IReadOnlySet<string> Operators { get; } = operators;
        public IReadOnlyList<(string Host, string Name)> Partners { get; } = partners;
        public List<GccPlacedCitation> Placed { get; } = [];
        public List<GccUncitedFigures> Uncited { get; } = [];
    }

    /// <summary>
    /// <paramref name="lede"/> and <paramref name="sections"/> with a citation after every sentence that
    /// states a figure a page of <paramref name="inputs"/> states.
    /// </summary>
    public static Result Cite(Section lede, IReadOnlyList<Section> sections, Inputs inputs)
    {
        var partners = inputs.PartnerNamesByHost
            .Select(pair => (Host: pair.Key, Name: pair.Value.Trim()))
            .Where(partner => partner.Host.Length > 0 && partner.Name.Length > 0)
            .OrderByDescending(partner => partner.Name.Length)
            .ToList();
        var state = new State(
            SourcesOf(inputs, partners),
            NumbersOf(inputs.CompetitorPages),
            GccFigureGrammar.NumbersIn(inputs.OperatorsOwnWords),
            partners);

        var citedLede = CiteSection(lede, state);
        var citedSections = sections.Select(section => CiteSection(section, state)).ToList();
        return new Result(citedLede, citedSections, state.Placed, state.Uncited);
    }

    /// <summary>
    /// What the operator is told when sentences could be cited to no page: how many, each one whole with
    /// its figures, the heading it stands under and why. Null when every figure was cited.
    /// </summary>
    /// <param name="type">"pillar", "blog" or "tool page".</param>
    public static string? Line(string type, Result result)
    {
        if (result.Uncited.Count == 0) return null;

        static string One(GccUncitedFigures uncited) =>
            $"\"{uncited.Sentence}\" ({string.Join(", ", uncited.Figures)}), under \"{uncited.Heading}\": {uncited.Why}";

        var count = result.Uncited.Count;
        var head = count == 1
            ? $"The {type}: 1 sentence states a figure with no page to cite it to: {One(result.Uncited[0])}."
            : $"The {type}: {count} sentences state a figure with no page to cite it to: {string.Join("; ", result.Uncited.Select(One))}.";
        return head + " Every other figure in the opening and the body is cited to the page it is on.";
    }

    private static IReadOnlyList<Source> SourcesOf(Inputs inputs, IReadOnlyList<(string Host, string Name)> partners)
    {
        var partnerHosts = partners.Select(partner => partner.Host).ToList();
        var sources = new List<Source>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in (inputs.PartnerPages ?? []).Concat(inputs.SitePages ?? []))
        {
            if (page is null || string.IsNullOrWhiteSpace(page.Url) || !seen.Add(page.Url.Trim())) continue;

            var host = GccRequiredToolMentions.HostKeyOf(page.Url);
            // PartnerOf answers the page's own host when no partner's site holds it; a source is a
            // partner's only when that host is one of the project's partners.
            var resolved = GccResearchSelector.PartnerOf(page.Url, partnerHosts);
            var partnerHost = inputs.PartnerNamesByHost.ContainsKey(resolved) ? resolved : string.Empty;
            var name = partnerHost.Length > 0 && inputs.PartnerNamesByHost.TryGetValue(partnerHost, out var partnerName)
                && !string.IsNullOrWhiteSpace(partnerName)
                    ? partnerName.Trim()
                : inputs.PublisherHosts.Contains(host) && !string.IsNullOrWhiteSpace(inputs.PublisherName)
                    ? inputs.PublisherName.Trim()
                : host.Length > 0 ? host : page.Url.Trim();

            sources.Add(new Source(page.Url.Trim(), name, page.CrawledAtUtc, GccFigureGrammar.NumbersIn(TextOf(page)), partnerHost));
        }

        return sources;
    }

    /// <summary>Every number on every page of <paramref name="pages"/>, as the grammar reads them.</summary>
    private static IReadOnlySet<string> NumbersOf(IReadOnlyList<GccQuoteablePage>? pages)
    {
        var numbers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages ?? [])
        {
            if (page is null) continue;
            numbers.UnionWith(GccFigureGrammar.NumbersIn(TextOf(page)));
        }

        return numbers;
    }

    /// <summary>A page as text: its title, its headings and its passages, one to a line.</summary>
    private static string TextOf(GccQuoteablePage page)
    {
        var text = new StringBuilder();
        text.AppendLine(page.Title ?? string.Empty);
        foreach (var heading in page.Headings ?? []) text.AppendLine(heading?.Text ?? string.Empty);
        foreach (var passage in page.Paragraphs ?? []) text.AppendLine(passage ?? string.Empty);
        return text.ToString();
    }

    private static Section CiteSection(Section section, State state)
    {
        var paragraphs = new List<Paragraph>(section.Paragraphs.Count);
        var changed = false;
        foreach (var paragraph in section.Paragraphs)
        {
            switch (paragraph)
            {
                case TextParagraph text:
                {
                    var runs = CiteRuns(text.Runs, section.Heading, state, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    if (ReferenceEquals(runs, text.Runs)) paragraphs.Add(text);
                    else
                    {
                        paragraphs.Add(text with { Runs = runs });
                        changed = true;
                    }

                    break;
                }

                case ListParagraph list:
                {
                    // A list is one paragraph: a page cited on one item is not cited again on the next.
                    var citedHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var items = new List<IReadOnlyList<Run>>(list.Items.Count);
                    var listChanged = false;
                    foreach (var item in list.Items)
                    {
                        var runs = CiteRuns(item, section.Heading, state, citedHere);
                        items.Add(runs);
                        listChanged |= !ReferenceEquals(runs, item);
                    }

                    paragraphs.Add(listChanged ? list with { Items = items } : list);
                    changed |= listChanged;
                    break;
                }

                // A quotation carries its own cite; a code block, a term and a definition are not prose.
                default:
                    paragraphs.Add(paragraph);
                    break;
            }
        }

        var children = new List<Section>(section.Children.Count);
        foreach (var child in section.Children)
        {
            var cited = CiteSection(child, state);
            children.Add(cited);
            changed |= !ReferenceEquals(cited, child);
        }

        return changed ? section with { Paragraphs = paragraphs, Children = children } : section;
    }

    /// <summary>
    /// One paragraph's or one list item's runs with a citation after each sentence that states a figure
    /// a page states. The same list when nothing is cited.
    /// </summary>
    private static IReadOnlyList<Run> CiteRuns(IReadOnlyList<Run> runs, string heading, State state, HashSet<string> citedHere)
    {
        var joined = new StringBuilder();
        var links = new List<(int Start, int End)>();
        foreach (var run in runs)
        {
            var start = joined.Length;
            joined.Append(run.Text);
            if (!string.IsNullOrWhiteSpace(run.Href)) links.Add((start, joined.Length));
        }

        var text = joined.ToString();
        // A figure inside a link is part of a name code placed, not a claim.
        var figures = GccFigureGrammar.Find(text, links)
            .Where(figure => !links.Any(link => figure.Index >= link.Start && figure.Index < link.End))
            .ToList();
        if (figures.Count == 0) return runs;

        var insertions = new List<(int At, IReadOnlyList<Run> Cite)>();
        foreach (var sentence in GccInventedFigures.SentencesOf(text))
        {
            var inIt = figures.Where(figure => figure.Index >= sentence.Start && figure.Index < sentence.End).ToList();
            if (inIt.Count == 0) continue;

            var sentenceText = text[sentence.Start..sentence.End].Trim();
            var written = inIt.Select(figure => figure.Written).ToList();
            var choice = Choose(sentenceText, inIt, state);
            if (choice.Source is null)
            {
                state.Uncited.Add(new GccUncitedFigures(heading, sentenceText, written, choice.Why ?? OnNoPage));
                continue;
            }

            // Cited on an earlier sentence of this paragraph: covered.
            if (!citedHere.Add(choice.Source.Url)) continue;

            insertions.Add((TerminalOffset(text, sentence), CitationRuns(choice.Source)));
            state.Placed.Add(new GccPlacedCitation(
                heading, sentenceText, written, choice.Source.Url, choice.Source.Name, choice.Source.AsOf, choice.AlsoOn));
        }

        return insertions.Count == 0 ? runs : Insert(runs, insertions);
    }

    /// <summary>The page a sentence's figures are cited to, or why none is.</summary>
    private static Choice Choose(string sentence, IReadOnlyList<GccFigure> figures, State state)
    {
        var candidates = state.Sources
            .Where(source => figures.All(figure => source.Numbers.Contains(figure.Value)))
            .ToList();

        // Longest name first, so a name that contains another ("Bill.com" and "Bill") is read whole.
        var named = state.Partners.FirstOrDefault(partner => GccRequiredToolMentions.FindAsWord(sentence, partner.Name) is not null);
        if (named.Name is not null)
        {
            var own = candidates.Where(candidate => string.Equals(candidate.PartnerHost, named.Host, StringComparison.OrdinalIgnoreCase)).ToList();
            if (own.Count > 0) return new Choice(own[0], own.Count - 1, null);
            if (candidates.Count > 0)
            {
                return new Choice(
                    null, 0,
                    $"the sentence names {named.Name}, whose pages do not state it; it is on {candidates[0].Name}'s page");
            }
        }

        if (candidates.Count > 0) return new Choice(candidates[0], candidates.Count - 1, null);

        if (figures.All(figure => state.Operators.Contains(figure.Value))) return new Choice(null, 0, FromTheBrief);
        if (figures.All(figure => state.Competitor.Contains(figure.Value))) return new Choice(null, 0, OnACompetitorsPage);
        if (figures.All(figure => state.Sources.Any(source => source.Numbers.Contains(figure.Value))))
        {
            return new Choice(null, 0, OnDifferentPages);
        }

        return new Choice(null, 0, OnNoPage);
    }

    /// <summary>
    /// Where a citation goes in a sentence: before its full stop, question mark or exclamation mark, and
    /// before the closing quotation mark or bracket that follows one; at the sentence's end when it has
    /// no such mark.
    /// </summary>
    internal static int TerminalOffset(string text, (int Start, int End) sentence)
    {
        var end = sentence.End;
        while (end > sentence.Start && char.IsWhiteSpace(text[end - 1])) end--;

        var beforeClosers = end;
        while (beforeClosers > sentence.Start && GccInventedFigures.Closers.Contains(text[beforeClosers - 1])) beforeClosers--;

        var mark = beforeClosers;
        while (mark > sentence.Start && text[mark - 1] is '.' or '!' or '?') mark--;

        return mark < beforeClosers ? mark : end;
    }

    /// <summary>" (" then the name, linked, then ", as of Oct 13, 2026)" or ")" when the page carries no date.</summary>
    private static IReadOnlyList<Run> CitationRuns(Source source)
    {
        var tail = source.AsOf is { } asOf
            ? $", as of {asOf.UtcDateTime.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)})"
            : ")";
        return [new Run(" ("), new Run(source.Name, Href: source.Url), new Run(tail)];
    }

    /// <summary>
    /// <paramref name="runs"/> with each citation inserted at its character offset. A run is split where
    /// the offset falls inside it, keeping its formatting on both halves; a linked run is never split,
    /// so an offset inside one puts the citation after it.
    /// </summary>
    private static IReadOnlyList<Run> Insert(IReadOnlyList<Run> runs, List<(int At, IReadOnlyList<Run> Cite)> insertions)
    {
        insertions.Sort((a, b) => a.At.CompareTo(b.At));
        var result = new List<Run>(runs.Count + insertions.Count * 3);
        var next = 0;
        var offset = 0;
        foreach (var run in runs)
        {
            var text = run.Text ?? string.Empty;
            var start = offset;
            var end = offset + text.Length;
            offset = end;
            var linked = !string.IsNullOrWhiteSpace(run.Href);
            var emitted = start;
            while (next < insertions.Count && insertions[next].At < end)
            {
                var at = Math.Max(insertions[next].At, start);
                if (linked && at > start) break;
                if (at > emitted) result.Add(run with { Text = text[(emitted - start)..(at - start)] });
                result.AddRange(insertions[next].Cite);
                emitted = at;
                next++;
            }

            if (emitted < end) result.Add(run with { Text = text[(emitted - start)..] });
        }

        for (; next < insertions.Count; next++) result.AddRange(insertions[next].Cite);
        return result;
    }
}
