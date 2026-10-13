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
/// <param name="Whole">True when all of the research is carried: it fits the room the request has.</param>
/// <param name="Labels">The labels of the sections the request writes.</param>
/// <param name="Chosen">What it would carry, partner by partner, best first within each.</param>
/// <param name="LeftOut">What it would not.</param>
/// <param name="Room">The room it was chosen for, in characters as printed; null when the research is
/// carried whole without one being asked of it.</param>
/// <param name="Shares">Each partner's part of that room; empty when the research is carried whole.</param>
public sealed record GccResearchSelection(
    bool Whole,
    IReadOnlyList<string> Labels,
    IReadOnlyList<GccChosenPassage> Chosen,
    IReadOnlyList<GccResearchPassage> LeftOut,
    int? Room,
    IReadOnlyDictionary<string, int> Shares);

/// <summary>
/// Chooses, for one request of a page, the passages of partner research that request carries when not
/// all of them fit what its model accepts.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one limit is the model's.</b> Research that fits the room a request has is carried whole,
/// and nothing here is asked. Until 2026-10-12 this class held three numbers of its own: research was
/// whole up to 350,000 characters and otherwise 40,000 for each partner, 240,000 in all. They were
/// sized for a model that accepted 128,000 tokens and they cut research that fitted (Jeff, 2026-10-12:
/// "STOP NOT USING DATA, EVEN IF THAT MEANS IT COSTS MORE"). The room is now what the request's model
/// accepts, less everything else the request carries and the reply's reservation, worked out for each
/// request by <c>GccGenerateService.RequestThatFitsAsync</c> from a count of the request itself
/// (<see cref="GccRequestSize"/>).
/// </para>
/// <para>
/// <b>When the research is more than the room, in the order the rules apply.</b>
/// <list type="number">
/// <item>Before any partner research is left out, the competitor pages beyond the first few are
/// (<see cref="GccCompetitorOutlines"/>): they are read for difference and never cited, and the
/// partners are what the page is about. This class is asked only if the request is still over.</item>
/// <item>The room is shared between the partners equally, so no partner is crowded out: the page names
/// every one and needs something to say about each. A partner with less research than its share is
/// carried whole, and what it does not use is shared between the others.</item>
/// <item>A partner is carried up to its share. Passages labelled for the request's sections come
/// first, the strongest first, then general ones, then the partner's other passages. A passage an
/// earlier request of the same page carried counts half as much each time, and of the partner's
/// other passages the ones carried least come first, so the page as a whole sees more of the
/// research.</item>
/// </list>
/// </para>
/// <para>
/// <b>Why a partner is filled to its share.</b> From the 14:20 UTC run of 2026-10-11, the first whose
/// choice was recorded (project "Automated Spreadsheet Data", 213 passages, 250,717 characters). No
/// passage was general: every one was found by the brief's problem or by one of its rows, and each
/// gives a label. And the research held almost nothing for some sections: 8 passages of 213 for
/// getting it running, 7 for results and proof. The first version left a passage labelled only for
/// other sections out, so the request that writes two sections on getting it running would have
/// carried a fifth of the research. Labels decide the order a partner's passages are taken in, never
/// whether a section is shown enough.
/// </para>
/// <para>
/// <b>What choosing costs.</b> The same run measured what repeating the research is worth: a pillar's
/// five body requests each sent about 68,300 tokens, and on the second to the fifth 66,432 of them
/// were billed at the lower price for text OpenAI had just been sent. A request that carries a choice
/// of its own carries different text from the one before it and loses that. It is one more reason
/// nothing is chosen while everything fits.
/// </para>
/// <para>
/// <b>Plain code.</b> No network and no model, and the same passages in the same order with the same
/// room give the same choice. <see cref="GccResearchPerRequest"/> puts each choice on the run's record.
/// </para>
/// </remarks>
public static class GccResearchSelector
{
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
    /// The passages the research block prints, each with its labels: every passage of every page.
    /// Until 2026-10-12 this was a page's first six, as the block printed them; nothing is cut by a
    /// count now, here or there.
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
        // The searches as the research block numbers them, so a passage's last line is counted as printed.
        var searches = GccPageResearch.SearchesOf(pages ?? []);
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
            foreach (var heading in page.Headings ?? [])
            {
                pageChars += (heading?.Text?.Length ?? 0) + 6 + line;
            }

            var scored = page.Scores is not null && page.Scores.Count == page.Paragraphs.Count;
            for (var i = 0; i < page.Paragraphs.Count; i++)
            {
                // The passage's own line, and the line after it that says which searches found it.
                var foundBy = page.SearchesThatFound(i).Count;
                var numbers = GccGenerateService.FoundForNumbers(page, i, searches);
                var foundForChars = numbers.Length == 0
                    ? 0
                    : 3 + GccGenerateService.FoundForLabel.Length + numbers.Length + line;
                passages.Add(new GccResearchPassage(
                    host,
                    url,
                    i,
                    (page.Paragraphs[i]?.Length ?? 0) + 2 + line + foundForChars,
                    pageChars,
                    GccPassageClassifier.LabelsOf(page, i),
                    foundBy,
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

    /// <summary>All of <paramref name="passages"/>, carried whole: nothing chosen and nothing left out.</summary>
    public static GccResearchSelection Everything(
        IReadOnlyList<GccResearchPassage> passages, IReadOnlyList<string> labels, int? room = null) =>
        new(
            Whole: true,
            labels,
            [.. passages.Select(passage => new GccChosenPassage(passage, WhyWhole))],
            [],
            room,
            new Dictionary<string, int>(StringComparer.Ordinal));

    /// <summary>
    /// Each partner's part of <paramref name="room"/>: an equal part, except that a partner with less
    /// research than that is given what it has and the rest is shared between the others.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> SharesOf(IReadOnlyList<GccResearchPassage> passages, int room)
    {
        var held = new List<(string Host, int Chars)>();
        foreach (var passage in passages)
        {
            if (held.Any(partner => partner.Host == passage.Host)) continue;
            held.Add((passage.Host, CharsOf(passages.Where(other => other.Host == passage.Host))));
        }

        // The partners with the least research first: each takes what it has when that is under an
        // equal part of what is left, and leaves the remainder to the ones after it.
        var shares = new Dictionary<string, int>(StringComparer.Ordinal);
        var left = Math.Max(0, room);
        var waiting = held.Count;
        foreach (var (host, chars) in held.OrderBy(partner => partner.Chars))
        {
            var share = Math.Min(chars, left / waiting);
            shares[host] = share;
            left -= share;
            waiting--;
        }

        return shares;
    }

    /// <summary>
    /// What a request writing sections labelled <paramref name="labels"/> would carry of
    /// <paramref name="passages"/> when it has <paramref name="room"/> for them.
    /// </summary>
    /// <param name="shownBefore">How many earlier requests of the same page carried each passage, by
    /// its page and its place on it. A passage not listed was carried by none.</param>
    /// <param name="room">The most partner research the request can carry, in characters as printed
    /// (<see cref="CharsOf"/>). Research of that size or less is carried whole.</param>
    public static GccResearchSelection Select(
        IReadOnlyList<GccResearchPassage> passages,
        IReadOnlyList<string> labels,
        IReadOnlyDictionary<(string Url, int Position), int> shownBefore,
        int room)
    {
        if (CharsOf(passages) <= room) return Everything(passages, labels, room);

        var shares = SharesOf(passages, room);
        var chosen = new List<GccChosenPassage>();
        var leftOut = new List<GccResearchPassage>();
        foreach (var host in shares.Keys.OrderBy(host => IndexOfFirst(passages, host)))
        {
            var limit = shares[host];
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

        return new GccResearchSelection(Whole: false, labels, chosen, leftOut, room, shares);
    }

    /// <summary>Where <paramref name="host"/>'s first passage is, so partners keep the order the research has them in.</summary>
    private static int IndexOfFirst(IReadOnlyList<GccResearchPassage> passages, string host)
    {
        for (var i = 0; i < passages.Count; i++)
        {
            if (passages[i].Host == host) return i;
        }

        return passages.Count;
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

/// <summary>
/// What a request carries of the research that can give way when the request is more than its model
/// accepts. Everything else in a request is whole in every request of the page.
/// </summary>
/// <param name="Passages">The passages of partner research it carries, by page and place on it; null
/// for all of them.</param>
/// <param name="EveryCompetitorPage">Whether it shows every competitor page the searches returned,
/// each with its outline. False is the first thing that gives way: the request then shows the ones
/// <see cref="GccCompetitorOutlines.Shown"/> keeps.</param>
public sealed record GccRequestCarries(
    IReadOnlySet<(string Url, int Position)>? Passages, bool EveryCompetitorPage = true)
{
    /// <summary>All of the research: what every request carries unless that is more than its model accepts.</summary>
    public static readonly GccRequestCarries Everything = new(Passages: null);

    /// <summary>Whether nothing was left out of the request.</summary>
    public bool IsEverything => Passages is null && EveryCompetitorPage;
}

/// <summary>What one request carries of its page's partner research.</summary>
/// <param name="Record">The run's record of it: the request's size, what the research holds, what the
/// request carries, partner by partner, and each passage chosen and left out.</param>
/// <param name="Carried">The passages the request is sent, by page and place on it, when they were
/// chosen for it; null when the request is sent all of the research.</param>
public sealed record GccRequestResearch(object Record, IReadOnlySet<(string Url, int Position)>? Carried);

/// <summary>One request's size, counted before it is sent (<see cref="GccRequestSize"/>).</summary>
/// <param name="Model">The model the request is sent to.</param>
/// <param name="Accepts">What that model accepts in one request: what is sent and the reply's reservation together.</param>
/// <param name="Reply">The tokens reserved for the reply.</param>
/// <param name="Tokens">The request as it is sent.</param>
/// <param name="WithEverything">What it would be with all of the page's partner research; the same as
/// <paramref name="Tokens"/> when nothing was left out.</param>
public sealed record GccRequestCount(string Model, int Accepts, int Reply, int Tokens, int WithEverything);

/// <summary>
/// What each request of one page carries of the page's partner research: the opening, then each body
/// request, in the order they are written.
/// </summary>
/// <remarks>
/// <para>
/// One per page being written, because what a request carries depends on what the requests before it
/// carried. It holds the page's partner research as its research block prints it, answers what a
/// request would carry in a given room (<see cref="Choose"/>), and writes the run's record of what a
/// request was sent (<see cref="Send"/>). Whether a request is over what its model accepts, and what
/// room it has, is worked out from a count of the request itself by
/// <c>GccGenerateService.RequestThatFitsAsync</c>; research that fits is carried whole and nothing is
/// chosen.
/// </para>
/// <para>
/// <b>Every page that carries partner research goes through it</b>: the pillar and the blog since
/// 2026-10-12 (Jeff: "Research classification: finish this off"), the tool page since the same day's
/// second change. A request whose sections name no label, which is every request of a tool page, is
/// chosen for by the search's own order.
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

    /// <param name="partnerPages">The page's partner research, as its research block prints it
    /// (<see cref="GccPageResearch.PagesOf"/>).</param>
    /// <param name="partnerUrls">The project's declared partners, by which a page is one partner's.</param>
    public GccResearchPerRequest(IReadOnlyList<GccQuoteablePage>? partnerPages, IReadOnlyList<string>? partnerUrls) =>
        _passages = GccResearchSelector.PassagesOf(partnerPages, partnerUrls);

    /// <summary>What printing all of the research adds to a request, in characters.</summary>
    public int Chars => GccResearchSelector.CharsOf(_passages);

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
    /// What a request writing sections labelled <paramref name="labels"/> would carry: all of the
    /// research when no room is given or the research fits it, and otherwise the passages chosen for
    /// that room. Nothing is counted as carried until the request is sent (<see cref="Send"/>).
    /// </summary>
    /// <param name="room">The most partner research the request can carry, in characters as printed;
    /// null when the request with all of it is within what its model accepts.</param>
    public GccResearchSelection Choose(IReadOnlyList<string> labels, int? room) =>
        room is { } chars
            ? GccResearchSelector.Select(_passages, labels, _shown, chars)
            : GccResearchSelector.Everything(_passages, labels);

    /// <summary>The passages a request is sent, by page and place on it, or null when it is sent all of the research.</summary>
    public static IReadOnlySet<(string Url, int Position)>? CarriedOf(GccResearchSelection selection) =>
        selection.Whole
            ? null
            : selection.Chosen.Select(chosen => (chosen.Passage.Url, chosen.Passage.Position)).ToHashSet();

    /// <summary>
    /// The record of a request sent <paramref name="selection"/>, which is counted as carried for the
    /// requests after it.
    /// </summary>
    /// <param name="request">The request's name on the run's record.</param>
    /// <param name="call">Which body request it is, from one; zero for the page's opening.</param>
    /// <param name="of">How many body requests the page has.</param>
    /// <param name="count">The request's size as counted before it is sent, when it was counted.</param>
    /// <param name="competitorPagesLeftOut">The competitor pages the request does not show, by address,
    /// when it was over what its model accepts with all of them; null when it shows every one.</param>
    public GccRequestResearch Send(
        GccResearchSelection selection,
        string request,
        int call,
        int of,
        GccRequestCount? count = null,
        IReadOnlyList<string>? competitorPagesLeftOut = null)
    {
        foreach (var chosen in selection.Chosen)
        {
            var key = (chosen.Passage.Url, chosen.Passage.Position);
            _shown[key] = _shown.GetValueOrDefault(key) + 1;
        }

        var carried = selection.Chosen.Select(chosen => chosen.Passage).ToList();
        var record = new
        {
            batch = request,
            call,
            of,
            labels = selection.Labels,
            // All of the research is carried: nothing was chosen between. Otherwise the request is
            // sent what is listed under "chosen" and nothing listed under "leftOut".
            whole = selection.Whole,
            // The request as counted before it is sent, against what its model accepts. withReply is
            // what is held to that limit; withEverything is the request with all of the research.
            request = count is null
                ? null
                : new
                {
                    model = count.Model,
                    accepts = count.Accepts,
                    reply = count.Reply,
                    tokens = count.Tokens,
                    withReply = count.Tokens + count.Reply,
                    withEverything = count.WithEverything,
                },
            // The competitor pages that gave way, each with its outline, by address. They go before any
            // partner research does; null when the request shows every one.
            competitorPagesLeftOut,
            // The room the choice was made for, in characters as printed; null when everything fits.
            room = selection.Room,
            research = Size(_passages),
            carried = Size(carried),
            partners = HostsOf(_passages)
                .Select(host => new
                {
                    host,
                    research = Size([.. _passages.Where(passage => passage.Host == host)]),
                    carried = Size([.. carried.Where(passage => passage.Host == host)]),
                    // Its part of the room, when the research was chosen from.
                    share = selection.Shares.TryGetValue(host, out var share) ? share : (int?)null,
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

        return new GccRequestResearch(record, CarriedOf(selection));
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

    /// <summary>How many passages carry each label, in the labels' own order, then how many carry none.</summary>
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
