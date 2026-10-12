using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>One passage of partner research, as a request would carry it.</summary>
/// <param name="Host">The partner it belongs to (<see cref="GccResearchSelector.PartnerOf"/>): the
/// project's partner whose site its page is on, by the one key anything is bucketed by partner with
/// (<see cref="GccRequiredToolMentions.HostKeyOf"/>).</param>
/// <param name="Url">The page it is on.</param>
/// <param name="Position">Its place among that page's passages.</param>
/// <param name="Chars">What printing it adds to a request.</param>
/// <param name="PageChars">What printing its page's own lines adds, once however many of the page's
/// passages a request carries.</param>
/// <param name="Labels">The labels it is given, each with its strength; empty for a general passage.</param>
/// <param name="FoundBySearches">How many of the brief's searches found it.</param>
/// <param name="Score">What the search scored it, or zero when the page does not say.</param>
public sealed record GccResearchPassage(
    string Host,
    string Url,
    int Position,
    int Chars,
    int PageChars,
    IReadOnlyDictionary<string, int> Labels,
    int FoundBySearches,
    double Score);

/// <summary>A passage a request would carry, and why.</summary>
/// <param name="Why">One of the selector's four reasons: the whole research is carried, the passage is
/// labelled for the request's sections, it is general, or it is another of its partner's passages
/// taken while there was room.</param>
public sealed record GccChosenPassage(GccResearchPassage Passage, string Why);

/// <summary>What one request would carry of a page's partner research.</summary>
/// <param name="Whole">True when the research is small enough to be carried whole, as it is today.</param>
/// <param name="Labels">The labels of the sections the request writes.</param>
/// <param name="Chosen">What it would carry, partner by partner, best first within each.</param>
/// <param name="LeftOut">What it would not.</param>
public sealed record GccResearchSelection(
    bool Whole,
    IReadOnlyList<string> Labels,
    IReadOnlyList<GccChosenPassage> Chosen,
    IReadOnlyList<GccResearchPassage> LeftOut);

/// <summary>
/// Chooses, for one request of a pillar or a blog, the passages of partner research that request
/// would carry (plans/research-classification.md, "What each request then carries").
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Every request that writes part of a pillar or a blog carries all of the research, every
/// passage found on every partner's site. On 2026-10-10 that put both pages over the model's limit,
/// and the room made since is about 9% on the project it happened to. A request that writes two
/// sections needs the passages for those two.
/// </para>
/// <para>
/// <b>The rules, in the order they apply.</b>
/// <list type="number">
/// <item>Research of <see cref="WholeUpToChars"/> or less is carried whole. A project that fits is not
/// changed.</item>
/// <item>Every partner has the same limit, <see cref="MaxCharsPerPartner"/> or its equal part of
/// <see cref="MaxCharsInAll"/> when there are many partners, so no partner is crowded out: the page
/// names every one and needs something to say about each.</item>
/// <item>A partner is carried up to that limit. Passages labelled for the request's sections come
/// first, the strongest first, then general ones, then the partner's other passages. A passage an
/// earlier request of the same page carried counts half as much each time, and of the partner's
/// other passages the ones carried least come first, so the page as a whole sees more of the
/// research.</item>
/// </list>
/// </para>
/// <para>
/// <b>Why a partner is filled to its limit, and why research is whole up to
/// <see cref="WholeUpToChars"/>.</b> Both are from the 14:20 UTC run of 2026-10-11, the first with
/// this watching (project "Automated Spreadsheet Data", 213 passages, 250,717 characters).
/// <list type="bullet">
/// <item>No passage was general: every one was found by the brief's problem or by one of its rows, and
/// each gives a label. And the research held almost nothing for some sections: 8 passages of 213 for
/// getting it running, 7 for results and proof. The first version left a passage labelled only for
/// other sections out, so the request that writes two sections on getting it running would have
/// carried 54,630 characters, a fifth of the research, most of it make-weight. Labels now decide the
/// order a partner's passages are taken in, never whether a section is shown enough.</item>
/// <item>The same run measured what repeating the research is worth. A pillar's five body requests
/// each sent about 68,300 tokens, and on the second to the fifth 66,432 of them were billed at half
/// price as text OpenAI had just been sent. A request that carries a choice of its own carries
/// different text from the one before it and loses that. Chosen down to three quarters, as a ceiling
/// of 240,000 would have made it, that project's pillar would have cost about 30% more to send less.
/// It was also nowhere near the model's limit: 76,520 of 128,000 with the reply's reservation. At
/// the ceiling a request is about 97,000 of 128,000.</item>
/// </list>
/// </para>
/// <para>
/// <b>Plain code.</b> No network and no model, and the same passages in the same order give the same
/// choice. It runs on every pillar and blog. <see cref="GccResearchPerRequest"/> puts each choice on the
/// run's record and says which requests are sent it.
/// </para>
/// </remarks>
public static class GccResearchSelector
{
    /// <summary>The most of one partner's research a request carries, in characters as printed.</summary>
    public const int MaxCharsPerPartner = 40_000;

    /// <summary>The most partner research a request carries in all when it is chosen, in characters as printed.</summary>
    public const int MaxCharsInAll = 240_000;

    /// <summary>
    /// Research of this size or less is carried whole, in characters as printed. Above it a request is
    /// too near the model's limit to leave alone; under it, choosing sends less and costs more.
    /// </summary>
    public const int WholeUpToChars = 350_000;

    public const string WhyWhole = "whole";
    public const string WhyLabelled = "labelled";
    public const string WhyGeneral = "general";
    public const string WhyOther = "other";

    /// <summary>
    /// The partner a page belongs to: the project's partner whose host the page is on, or on a
    /// subdomain of, and otherwise the page's own host.
    /// </summary>
    /// <remarks>
    /// Every partner has the same limit, so what counts as one partner decides how much of it a request
    /// carries. Told apart by the page's host alone, a partner's help or documentation site
    /// ("help.partner.com") was a second partner with a limit of its own, and each real partner's limit
    /// was an equal part of a larger number of hosts. The first real run had none: five partners, five
    /// hosts. Two partners of which one is a subdomain of the other are told apart by the longer host.
    /// </remarks>
    /// <param name="partnerHosts">The project's partners, each by <see cref="GccRequiredToolMentions.HostKeyOf"/>.</param>
    public static string PartnerOf(string? url, IReadOnlyList<string> partnerHosts)
    {
        var host = GccRequiredToolMentions.HostKeyOf(url ?? string.Empty);
        if (host.Length == 0) return host;

        string? partner = null;
        foreach (var candidate in partnerHosts)
        {
            if (host == candidate) return candidate;
            if (candidate.Length > 0
                && host.EndsWith("." + candidate, StringComparison.Ordinal)
                && (partner is null || candidate.Length > partner.Length))
            {
                partner = candidate;
            }
        }

        return partner ?? host;
    }

    /// <summary>
    /// The passages a writer is sent today, each with its labels: every page's first
    /// <see cref="GccResearchCaps.MaxParagraphsPerPage"/>, which is what the research block prints.
    /// </summary>
    /// <param name="partnerUrls">The project's declared partners. A page on a partner's subdomain is
    /// that partner's (<see cref="PartnerOf"/>); with none given, a page's partner is its own host.</param>
    public static IReadOnlyList<GccResearchPassage> PassagesOf(
        IReadOnlyList<GccQuoteablePage>? pages, IReadOnlyList<string>? partnerUrls = null)
    {
        var passages = new List<GccResearchPassage>();
        var line = Environment.NewLine.Length;
        IReadOnlyList<string> partnerHosts =
        [
            .. (partnerUrls ?? [])
                .Select(partnerUrl => GccRequiredToolMentions.HostKeyOf(partnerUrl ?? string.Empty))
                .Where(partnerHost => partnerHost.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];
        foreach (var page in pages ?? [])
        {
            // Research is read back from stored JSON, where a field the record calls required can be
            // missing. The block prints such a page as it is, so it is counted as it is: this runs on
            // every pillar and blog and must not be what stops one.
            if (page?.Paragraphs is null) continue;
            var url = page.Url ?? string.Empty;
            var host = PartnerOf(url, partnerHosts);
            // The page's own lines as GccGenerateService.BuildResearchBlock prints them: its title and
            // address, its headings, and the blank line after its passages.
            var pageChars = (page.Title?.Length ?? 0) + url.Length + 3 + line + line;
            foreach (var heading in (page.Headings ?? []).Take(GccResearchCaps.MaxHeadingsPerPage))
            {
                pageChars += (heading?.Text?.Length ?? 0) + 6 + line;
            }

            var scored = page.Scores is not null && page.Scores.Count == page.Paragraphs.Count;
            var printed = Math.Min(page.Paragraphs.Count, GccResearchCaps.MaxParagraphsPerPage);
            for (var i = 0; i < printed; i++)
            {
                passages.Add(new GccResearchPassage(
                    host,
                    url,
                    i,
                    (page.Paragraphs[i]?.Length ?? 0) + 2 + line,
                    pageChars,
                    GccPassageClassifier.LabelsOf(page, i),
                    page.SearchesThatFound(i).Count,
                    scored ? page.Scores![i]?.Score ?? 0 : 0));
            }
        }

        return passages;
    }

    /// <summary>What printing <paramref name="passages"/> costs: each passage, and each page's own lines once.</summary>
    public static int CharsOf(IEnumerable<GccResearchPassage> passages)
    {
        var chars = 0;
        var pages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var passage in passages)
        {
            chars += passage.Chars;
            if (pages.Add(passage.Url)) chars += passage.PageChars;
        }

        return chars;
    }

    /// <summary>
    /// What a request writing sections labelled <paramref name="labels"/> would carry of
    /// <paramref name="passages"/>.
    /// </summary>
    /// <param name="shownBefore">How many earlier requests of the same page carried each passage, by
    /// its page and its place on it. A passage not listed was carried by none.</param>
    public static GccResearchSelection Select(
        IReadOnlyList<GccResearchPassage> passages,
        IReadOnlyList<string> labels,
        IReadOnlyDictionary<(string Url, int Position), int> shownBefore)
    {
        if (CharsOf(passages) <= WholeUpToChars)
        {
            return new GccResearchSelection(
                Whole: true, labels, [.. passages.Select(passage => new GccChosenPassage(passage, WhyWhole))], []);
        }

        var hosts = new List<string>();
        foreach (var passage in passages)
        {
            if (!hosts.Contains(passage.Host, StringComparer.Ordinal)) hosts.Add(passage.Host);
        }

        // Over the ceiling there is at least one passage, so at least one host.
        var limit = Math.Min(MaxCharsPerPartner, MaxCharsInAll / hosts.Count);

        var chosen = new List<GccChosenPassage>();
        var leftOut = new List<GccResearchPassage>();
        foreach (var host in hosts)
        {
            var mine = passages.Where(passage => passage.Host == host).ToList();
            var taken = new HashSet<GccResearchPassage>(ReferenceEqualityComparer.Instance);
            var pagesIn = new HashSet<string>(StringComparer.Ordinal);
            var used = 0;

            bool Take(GccResearchPassage passage, string why)
            {
                var cost = passage.Chars + (pagesIn.Contains(passage.Url) ? 0 : passage.PageChars);
                if (used + cost > limit) return false;
                used += cost;
                pagesIn.Add(passage.Url);
                taken.Add(passage);
                chosen.Add(new GccChosenPassage(passage, why));
                return true;
            }

            // For this request: labelled for its sections, or general. Strongest first; of two as
            // strong, the one more of the brief's searches found, then the one the search scored
            // higher, then the one that came first.
            var forThisRequest = mine
                .Select(passage => (Passage: passage, Value: ValueOf(passage, labels, shownBefore)))
                .Where(ranked => ranked.Value > 0)
                .OrderByDescending(ranked => ranked.Value)
                .ThenByDescending(ranked => ranked.Passage.FoundBySearches)
                .ThenByDescending(ranked => ranked.Passage.Score)
                .ToList();
            foreach (var (passage, _) in forThisRequest)
            {
                // One that does not fit is passed over, and a shorter one after it may still fit.
                Take(passage, passage.Labels.Count == 0 ? WhyGeneral : WhyLabelled);
            }

            // Then the partner's other passages, while there is room: the ones earlier requests
            // carried least first, then the search's best. A section the research holds little for is
            // still shown as much of each partner as any other.
            foreach (var passage in mine
                .Where(passage => !taken.Contains(passage))
                .OrderBy(passage => shownBefore.GetValueOrDefault((passage.Url, passage.Position)))
                .ThenByDescending(passage => passage.Score)
                .ThenByDescending(passage => passage.FoundBySearches))
            {
                Take(passage, WhyOther);
            }

            leftOut.AddRange(mine.Where(passage => !taken.Contains(passage)));
        }

        return new GccResearchSelection(Whole: false, labels, chosen, leftOut);
    }

    /// <summary>
    /// What a passage is worth to a request: its strength for the strongest of the request's labels it
    /// carries, one for a general passage, nothing for a passage labelled only for other sections,
    /// halved for each earlier request of the page that carried it.
    /// </summary>
    internal static double ValueOf(
        GccResearchPassage passage,
        IReadOnlyList<string> labels,
        IReadOnlyDictionary<(string Url, int Position), int> shownBefore)
    {
        double worth = 0;
        if (passage.Labels.Count == 0)
        {
            worth = 1;
        }
        else
        {
            foreach (var label in labels)
            {
                if (passage.Labels.TryGetValue(label, out var strength) && strength > worth) worth = strength;
            }
        }

        var shown = shownBefore.GetValueOrDefault((passage.Url, passage.Position));
        for (var i = 0; i < shown; i++) worth /= 2;
        return worth;
    }
}

/// <summary>What one request carries of its page's partner research.</summary>
/// <param name="Record">The run's record of it: what the research holds, what the request carries,
/// partner by partner, and each passage chosen and left out.</param>
/// <param name="Carried">The passages the request is sent, by page and place on it, when they were
/// chosen for it; null when the request is sent all of the research.</param>
public sealed record GccRequestResearch(object Record, IReadOnlySet<(string Url, int Position)>? Carried);

/// <summary>
/// What each request of one pillar or one blog carries of the page's partner research: the opening,
/// then each body request, in the order they are written (plans/research-classification.md).
/// </summary>
/// <remarks>
/// <para>
/// One per page being written, because what a request carries depends on what the requests before it
/// carried. It reads the page's partner research and the labels of the sections a request writes, and
/// answers with the passages chosen (<see cref="GccResearchSelector.Select"/>) and the run's record of
/// the choice. Research that fits is carried whole and nothing is chosen.
/// </para>
/// <para>
/// <b>On for the blog since 2026-10-12, on Jeff's word ("Research classification: finish this off").</b>
/// A blog's requests are sent what is chosen for them. A pillar's are still sent everything, and what
/// each would have carried is recorded with <c>watch</c> true.
/// </para>
/// <para>
/// <b>What a page's checks read is not this.</b> The figure, money, quotation and heading checks on a
/// finished page read all of the research, so a page is never refused because a request was shown
/// less.
/// </para>
/// </remarks>
public sealed class GccResearchPerRequest
{
    private readonly IReadOnlyList<GccResearchPassage> _passages;
    private readonly Dictionary<(string Url, int Position), int> _shown = [];
    private readonly bool _sendsWhatIsChosen;

    /// <param name="partnerPages">The page's partner research, as its research block prints it.</param>
    /// <param name="partnerUrls">The project's declared partners, by which a page is one partner's.</param>
    /// <param name="sendsWhatIsChosen">Whether a request is sent what is chosen for it. False records
    /// the choice and sends everything.</param>
    public GccResearchPerRequest(
        IReadOnlyList<GccQuoteablePage>? partnerPages, IReadOnlyList<string>? partnerUrls, bool sendsWhatIsChosen)
    {
        _passages = GccResearchSelector.PassagesOf(partnerPages, partnerUrls);
        _sendsWhatIsChosen = sendsWhatIsChosen;
    }

    /// <summary>
    /// How the page's partner research divides by label, for the run's record: every passage counted
    /// under each label it is given, a passage with none under "general", and the same partner by
    /// partner. This is what shows whether a label has anything to choose from.
    /// </summary>
    public object LabelsRecord() => new
    {
        passages = _passages.Count,
        chars = GccResearchSelector.CharsOf(_passages),
        byLabel = CountsByLabel(_passages),
        partners = HostsOf(_passages)
            .Select(host => _passages.Where(passage => passage.Host == host).ToList())
            .Select(mine => new
            {
                host = mine[0].Host,
                passages = mine.Count,
                chars = GccResearchSelector.CharsOf(mine),
                byLabel = CountsByLabel(mine),
            })
            .ToList(),
    };

    /// <summary>The labels the sections of <paramref name="batch"/> name, each once, in their order.</summary>
    public static IReadOnlyList<string> LabelsOf(IReadOnlyList<SectionSlot> batch)
    {
        var labels = new List<string>();
        foreach (var slot in batch)
        {
            if (slot.ResearchLabel is { Length: > 0 } label && !labels.Contains(label, StringComparer.Ordinal))
            {
                labels.Add(label);
            }
        }

        return labels;
    }

    /// <summary>
    /// What a request writing sections labelled <paramref name="labels"/> carries, or null when it names
    /// no label and so has no choice made for it. Counts what was chosen as carried, for the requests
    /// after it.
    /// </summary>
    /// <param name="request">The request's name on the run's record.</param>
    /// <param name="call">Which body request it is, from one; zero for the page's opening.</param>
    /// <param name="of">How many body requests the page has.</param>
    public GccRequestResearch? For(IReadOnlyList<string> labels, string request, int call, int of)
    {
        if (labels.Count == 0) return null;

        var selection = GccResearchSelector.Select(_passages, labels, _shown);
        foreach (var chosen in selection.Chosen)
        {
            var key = (chosen.Passage.Url, chosen.Passage.Position);
            _shown[key] = _shown.GetValueOrDefault(key) + 1;
        }

        var carried = selection.Chosen.Select(chosen => chosen.Passage).ToList();
        var sendsIt = _sendsWhatIsChosen && !selection.Whole;
        var record = new
        {
            batch = request,
            call,
            of,
            labels,
            // The research fits and is carried whole: nothing was chosen between.
            whole = selection.Whole,
            // A choice was made and recorded, and the request was sent everything all the same.
            watch = !selection.Whole && !sendsIt,
            limits = new
            {
                perPartner = GccResearchSelector.MaxCharsPerPartner,
                inAll = GccResearchSelector.MaxCharsInAll,
                wholeUpTo = GccResearchSelector.WholeUpToChars,
            },
            research = Size(_passages),
            carried = Size(carried),
            partners = HostsOf(_passages)
                .Select(host => new
                {
                    host,
                    research = Size([.. _passages.Where(passage => passage.Host == host)]),
                    carried = Size([.. carried.Where(passage => passage.Host == host)]),
                    labelled = CountWhy(selection, host, GccResearchSelector.WhyLabelled),
                    general = CountWhy(selection, host, GccResearchSelector.WhyGeneral),
                    other = CountWhy(selection, host, GccResearchSelector.WhyOther),
                    leftOut = selection.LeftOut.Count(passage => passage.Host == host),
                })
                .ToList(),
            // Listed only when something was chosen between: research carried whole has nothing to review.
            chosen = selection.Whole
                ? null
                : selection.Chosen
                    .Select(chosen => new
                    {
                        url = chosen.Passage.Url,
                        passage = chosen.Passage.Position + 1,
                        why = chosen.Why,
                        labels = LabelNames(chosen.Passage),
                    })
                    .ToList(),
            leftOut = selection.Whole
                ? null
                : selection.LeftOut
                    .Select(passage => new
                    {
                        url = passage.Url,
                        passage = passage.Position + 1,
                        labels = LabelNames(passage),
                    })
                    .ToList(),
        };

        return new GccRequestResearch(
            record,
            sendsIt ? carried.Select(passage => (passage.Url, passage.Position)).ToHashSet() : null);
    }

    private static object Size(IReadOnlyList<GccResearchPassage> passages) => new
    {
        pages = passages.Select(passage => passage.Url).Distinct(StringComparer.Ordinal).Count(),
        passages = passages.Count,
        chars = GccResearchSelector.CharsOf(passages),
    };

    private static int CountWhy(GccResearchSelection selection, string host, string why) =>
        selection.Chosen.Count(chosen => chosen.Passage.Host == host && chosen.Why == why);

    private static IReadOnlyList<string> HostsOf(IReadOnlyList<GccResearchPassage> passages)
    {
        var hosts = new List<string>();
        foreach (var passage in passages)
        {
            if (!hosts.Contains(passage.Host, StringComparer.Ordinal)) hosts.Add(passage.Host);
        }

        return hosts;
    }

    private static IReadOnlyList<string> LabelNames(GccResearchPassage passage) =>
        passage.Labels.Count == 0
            ? [GccResearchLabel.General]
            : [.. GccResearchLabel.All.Where(passage.Labels.ContainsKey)];

    /// <summary>How many passages carry each label, in the plan's order, then how many carry none.</summary>
    private static IReadOnlyDictionary<string, int> CountsByLabel(IReadOnlyList<GccResearchPassage> passages)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var label in GccResearchLabel.All)
        {
            counts[label] = passages.Count(passage => passage.Labels.ContainsKey(label));
        }

        counts[GccResearchLabel.General] = passages.Count(passage => passage.Labels.Count == 0);
        return counts;
    }
}
