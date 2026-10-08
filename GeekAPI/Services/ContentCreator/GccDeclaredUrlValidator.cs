using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Whether a project's declared URLs -- the site, the partners, the competitors -- can be written from,
/// asked of the index itself. Asked when they are entered, on Profile save, which is where it does the
/// most good; and asked once more when Generate is pressed, before anything is spent.
/// </summary>
/// <remarks>
/// <para>
/// A URL is usable when the index names a crawl for it, that crawl is complete with extracted content
/// and enough pages and chunks by its own counters, <b>and</b> the index returns chunks when that crawl
/// is searched the way Generate searches it: by run and crawl type. The last test is the one that
/// matters. The counters are what the crawl recorded writing; they say nothing of what the index holds
/// now. On 2026-10-04 a competitor passed the counter check at save and its run returned "No chunks"
/// at Generate, which then wrote the pieces without it -- after the work was paid for.
/// </para>
/// <para>
/// Searching by crawl type also catches a URL whose crawl was indexed as a different type: Generate
/// filters on the type of the list the URL is declared in, and would find nothing either.
/// </para>
/// <para>
/// A search that fails -- the index unreachable or erroring -- is not an answer about any URL, so the
/// whole check refuses and asks for a retry rather than calling the URL unusable or usable.
/// </para>
/// </remarks>
public sealed class GccDeclaredUrlValidator
{
    private readonly IGeekCrawlerRagClient _rag;
    private readonly HttpGeekCrawlerRepository _crawlerRepo;
    private readonly ILogger<GccDeclaredUrlValidator> _logger;

    public GccDeclaredUrlValidator(
        IGeekCrawlerRagClient rag,
        HttpGeekCrawlerRepository crawlerRepo,
        ILogger<GccDeclaredUrlValidator> logger)
    {
        _rag = rag;
        _crawlerRepo = crawlerRepo;
        _logger = logger;
    }

    /// <summary>
    /// The URLs a project may be saved with: a refusal, or the usable partners and competitors to
    /// persist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The floor is measured on URLs that have evidence, not on URLs that were typed. Declaring six
    /// partners and finding one uncrawled used to disable the whole save, because the count rule read
    /// the declared list while a separate rule required every declared URL to be usable -- two rules
    /// over two different sets, so an extra URL could only ever hurt. Five good partners are five good
    /// partners whether a sixth was entered or not.
    /// </para>
    /// <para>
    /// The unusable ones are excluded from what is saved rather than merely ignored. A declared partner
    /// obliges Pillar, Blog and Tool to name it (<c>GccRequiredToolMentions</c>), so one with no
    /// evidence behind it buys a refusal at generate time, which by then the operator can do nothing
    /// about. Excluding it is what keeps that promise; saving it and hoping is what broke it.
    /// </para>
    /// <para>
    /// The site is not one of several. There is exactly one, the project is grounded on its run, and
    /// nothing else can stand in for it, so an unusable site URL is a refusal and never an exclusion.
    /// </para>
    /// </remarks>
    public async Task<GccDeclaredUrlVerdict> ForSaveAsync(
        string? siteUrl,
        Guid? projectSiteRunId,
        IReadOnlyList<string>? partnerUrls,
        IReadOnlyList<string>? competitorUrls,
        CancellationToken ct)
    {
        var site = (siteUrl ?? string.Empty).Trim();
        var partners = Clean(partnerUrls);
        var competitors = Clean(competitorUrls);

        // Declared counts first, because they cost nothing and the operator can act on them without
        // waiting for an index round trip. A list already shorter than the floor cannot reach it once
        // the unusable are removed, so this stays a sound early answer rather than a guess.
        var shortfalls = new[]
        {
            string.IsNullOrWhiteSpace(site)
                ? $"Project site URL: 0 declared, {GccDeclaredUrlEvidence.RequiredSiteUrls} required."
                : null,
            GccDeclaredUrlEvidence.WrongCount(
                "Partner URLs", partners.Count, GccDeclaredUrlEvidence.RequiredPartnerUrls),
            GccDeclaredUrlEvidence.WrongCount(
                "Competitor URLs", competitors.Count, GccDeclaredUrlEvidence.RequiredCompetitorUrls),
        }.Where(m => m is not null).ToList();
        if (shortfalls.Count > 0)
            return GccDeclaredUrlVerdict.Refused(string.Join(" ", shortfalls));

        if (projectSiteRunId is not { } siteRun || siteRun == Guid.Empty)
        {
            return GccDeclaredUrlVerdict.Refused(
                "projectSiteRunId is required. It is the crawl this project's content is grounded "
                + "on, and the index returns it alongside the answer about the site URL.");
        }

        var evidence = await EvidenceAsync(site, partners, competitors, ct);
        if (evidence.Unreachable is { } unreachable)
            return GccDeclaredUrlVerdict.Refused(unreachable + " Nothing was saved — try again.");

        if (evidence.Site.TryGetValue(site, out var siteReason))
        {
            return GccDeclaredUrlVerdict.Refused(
                $"The project site URL cannot be written from: {site} — {siteReason}. "
                + "It is the crawl this project is grounded on, so nothing else can stand in for it.");
        }

        var usablePartners = partners.Where(u => !evidence.Partners.ContainsKey(u)).ToList();
        var usableCompetitors = competitors.Where(u => !evidence.Competitors.ContainsKey(u)).ToList();
        var floors = new[]
        {
            Floor("Partner URLs", partners, usablePartners,
                GccDeclaredUrlEvidence.RequiredPartnerUrls, evidence.Partners),
            Floor("Competitor URLs", competitors, usableCompetitors,
                GccDeclaredUrlEvidence.RequiredCompetitorUrls, evidence.Competitors),
        }.Where(m => m is not null).ToList();
        if (floors.Count > 0)
            return GccDeclaredUrlVerdict.Refused(string.Join(" ", floors));

        return GccDeclaredUrlVerdict.Usable(usablePartners, usableCompetitors);
    }

    /// <summary>
    /// When Generate is pressed, before the run starts or anything is spent: every URL the project
    /// declares must still be usable. Refused, naming each one and why, when any is not -- nothing is
    /// dropped here, because the saved project is what the run promises to cover.
    /// </summary>
    public async Task<GccDeclaredUrlVerdict> ForGenerateAsync(GccProjectDto project, CancellationToken ct)
    {
        var site = (project.SiteUrl ?? string.Empty).Trim();
        if (site.Length == 0)
            return GccDeclaredUrlVerdict.Refused("The project has no site URL. Save the Profile with one before generating.");

        var partners = Clean(project.PartnerUrls);
        var competitors = Clean(project.CompetitorUrls);
        var evidence = await EvidenceAsync(site, partners, competitors, ct);
        if (evidence.Unreachable is { } unreachable)
            return GccDeclaredUrlVerdict.Refused(unreachable + " Nothing was started — try again.");

        var named = Named(new[] { site }, evidence.Site)
            .Concat(Named(partners, evidence.Partners))
            .Concat(Named(competitors, evidence.Competitors))
            .ToList();
        if (named.Count == 0)
            return GccDeclaredUrlVerdict.Usable(partners, competitors) with { SiteRunId = evidence.SiteRunId };

        return GccDeclaredUrlVerdict.Refused(
            "Nothing was started: these declared URLs cannot be written from now. "
            + string.Join("; ", named) + ". "
            + "Re-index them, or save the Profile again so the unusable ones are dropped.");
    }

    private static IEnumerable<string> Named(IEnumerable<string> urls, IReadOnlyDictionary<string, string> reasons) =>
        urls.Where(reasons.ContainsKey).Select(u => $"{u} — {reasons[u]}");

    /// <summary>
    /// Why each declared URL cannot be used, list by list; or why nothing could be asked. A URL is
    /// judged as a member of the list it is declared in, so the same URL in two lists gets an answer
    /// for each.
    /// </summary>
    /// <param name="SiteRunId">The crawl of the site the index resolves and that was searched. The run
    /// must be written from this crawl and no other.</param>
    private sealed record Evidence(
        string? Unreachable,
        IReadOnlyDictionary<string, string> Site,
        IReadOnlyDictionary<string, string> Partners,
        IReadOnlyDictionary<string, string> Competitors,
        Guid? SiteRunId = null);

    private async Task<Evidence> EvidenceAsync(
        string site,
        IReadOnlyList<string> partners,
        IReadOnlyList<string> competitors,
        CancellationToken ct)
    {
        var lists = await Task.WhenAll(
            AnswerAsync([site], CrawlTypes.ProjectSite, ct),
            AnswerAsync(partners, CrawlTypes.Partner, ct),
            AnswerAsync(competitors, CrawlTypes.Competitors, ct));

        var none = new Dictionary<string, string>();
        if (lists.Select(l => l.Unreachable).FirstOrDefault(u => u is not null) is { } unreachable)
            return new Evidence(unreachable, none, none, none);

        var siteRun = lists[0].Answers.Select(a => a.RunId).FirstOrDefault();
        return new Evidence(
            null, Reasons(lists[0]), Reasons(lists[1]), Reasons(lists[2]),
            Guid.TryParse(siteRun, out var siteRunId) ? siteRunId : null);
    }

    private static Dictionary<string, string> Reasons(GccDeclaredUrlAnswers answered)
    {
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in answered.Unanswered) reasons[url] = "the index returned no answer for it";
        foreach (var answer in answered.Answers)
        {
            if (answer.Reason is { } reason) reasons[answer.Url] = reason;
        }

        return reasons;
    }

    /// <summary>
    /// The one answer about each URL of one list: usable or not, and why not. The form's as-you-type
    /// feedback, the Profile save and the check before Generate all read this, so a URL cannot be
    /// green in one place and refused in another.
    /// </summary>
    /// <remarks>
    /// The list is required, because it is part of the question: Generate searches a URL's crawl as
    /// the kind of list it is declared in, so "is this usable" has no answer without it. It was
    /// optional for a few hours on 2026-10-05, searching unfiltered when absent -- a second, looser
    /// meaning of usable at the very place validation is meant to happen first.
    /// </remarks>
    /// <param name="crawlType">project-site, partner or competitors (<see cref="CrawlTypes"/>).</param>
    public async Task<GccDeclaredUrlAnswers> AnswerAsync(
        IReadOnlyList<string> urls,
        string crawlType,
        CancellationToken ct)
    {
        var declared = Clean(urls);
        if (declared.Count == 0) return new GccDeclaredUrlAnswers(null, [], []);

        var rows = await _rag.HostsIndexedAsync(declared, crawlType, ct);
        if (rows.Count == 0)
        {
            _logger.LogWarning(
                "Index unreachable while validating {Count} declared URL(s); refusing rather than guessing.",
                declared.Count);
            return GccDeclaredUrlAnswers.NotAsked(
                "The index could not be reached, so the declared URLs could not be checked.");
        }

        var byUrl = rows.ToDictionary(r => r.Url, StringComparer.OrdinalIgnoreCase);
        var unanswered = new List<string>();
        var answers = new Dictionary<string, GccDeclaredUrlAnswer>(StringComparer.OrdinalIgnoreCase);
        var toSearch = new List<(string Url, Guid RunId)>();
        foreach (var url in declared)
        {
            if (!byUrl.TryGetValue(url, out var row))
            {
                unanswered.Add(url);
                continue;
            }

            // Indexed is not usable. The run says what actually landed.
            GeekCrawlerRunDto? run = null;
            if (row.Indexed && Guid.TryParse(row.RunId, out var runId))
                run = await _crawlerRepo.GetRunAsync(runId, ct);
            var reason = GccDeclaredUrlEvidence.Unusable(row, run);
            answers[url] = new GccDeclaredUrlAnswer(
                row.Url, row.Host, row.Indexed, row.RunId, reason,
                run?.RagPagesEnglish, run?.RagChunksUpserted, run?.RagPagesSkippedUnusable);
            if (reason is null) toSearch.Add((url, run!.Id));
        }

        // What the counters cannot say: does the index hold chunks for this run, searched as Generate
        // searches it. In parallel -- a list is a handful of URLs, and each is one small query.
        var searched = await Task.WhenAll(toSearch.Select(async s =>
            (s.Url, s.RunId, Result: await _rag.QueryAsync(ProbeNeed, s.RunId, crawlType: crawlType, topK: 1, ct: ct))));
        foreach (var (url, runId, result) in searched)
        {
            if (result is null || result.Failed)
            {
                _logger.LogWarning("Index search failed for run {RunId} ({Url}) while validating declared URLs", runId, url);
                return GccDeclaredUrlAnswers.NotAsked(
                    $"The index could not be searched for {url}, so the declared URLs could not be checked.");
            }

            if (result.Pages.Count == 0)
            {
                answers[url] = answers[url] with
                {
                    Reason = "its crawl finished, but a search of the index finds nothing from it, so there "
                        + "is nothing to write with",
                };
            }
        }

        return new GccDeclaredUrlAnswers(null, declared.Where(answers.ContainsKey).Select(u => answers[u]).ToList(), unanswered);
    }

    /// <summary>Any question retrieves from a run that holds chunks; this one only asks whether it does.</summary>
    internal const string ProbeNeed = "what this company offers and the problems it solves";

    /// <summary>
    /// The shortfall once the unusable are excluded, as the message the operator gets, or null when
    /// the floor is met.
    /// </summary>
    private static string? Floor(
        string label,
        IReadOnlyList<string> declared,
        IReadOnlyList<string> usable,
        int required,
        IReadOnlyDictionary<string, string> reasons)
    {
        if (usable.Count >= required) return null;
        var excluded = declared
            .Where(reasons.ContainsKey)
            .Select(u => $"{u} — {reasons[u]}")
            .ToList();
        return $"{label}: {usable.Count} of {required} have usable crawl evidence. "
            + $"These cannot be written from: {string.Join("; ", excluded)}. "
            + "Crawl and index them, or declare others.";
    }

    private static List<string> Clean(IReadOnlyList<string>? urls) =>
        (urls ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>The answer about a project's declared URLs: a refusal, or the usable ones.</summary>
/// <param name="SiteRunId">On a pass before Generate: the crawl of the project's own site that was
/// checked. It is the crawl the run is written from.</param>
public sealed record GccDeclaredUrlVerdict(
    string? Refusal,
    IReadOnlyList<string> PartnerUrls,
    IReadOnlyList<string> CompetitorUrls,
    Guid? SiteRunId = null)
{
    public static GccDeclaredUrlVerdict Refused(string refusal) => new(refusal, [], []);

    public static GccDeclaredUrlVerdict Usable(IReadOnlyList<string> partners, IReadOnlyList<string> competitors) =>
        new(null, partners, competitors);
}

/// <summary>What the index says about one entered URL. Usable when <see cref="Reason"/> is null.</summary>
public sealed record GccDeclaredUrlAnswer(
    string Url,
    string? Host,
    bool Indexed,
    string? RunId,
    string? Reason,
    int? Pages,
    int? Chunks,
    int? SkippedUnusable)
{
    public bool Usable => Reason is null;
}

/// <summary>
/// The answers for a set of URLs; or, when the index could not be asked, why -- which is not an answer
/// about any of them. <see cref="Unanswered"/> are URLs the index said nothing about.
/// </summary>
public sealed record GccDeclaredUrlAnswers(
    string? Unreachable,
    IReadOnlyList<GccDeclaredUrlAnswer> Answers,
    IReadOnlyList<string> Unanswered)
{
    public static GccDeclaredUrlAnswers NotAsked(string why) => new(why, [], []);
}
