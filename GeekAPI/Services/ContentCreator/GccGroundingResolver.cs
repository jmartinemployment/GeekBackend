using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The evidence a draft was actually grounded on, or the reason it cannot be written.
/// </summary>
/// <remarks>
/// <paramref name="Refusal"/> is the whole point. The defect this replaces was that every grounding
/// block in the prompt was conditional — <c>if (research?.Quoteables is { Count: &gt; 0 })</c> and
/// friends — so absent evidence silently dropped out and generation continued, producing prose that
/// reads identically whether it was grounded or not. A refusal is information; a confident
/// ungrounded draft is not.
/// </remarks>
public sealed record GccGroundingOutcome(
    IReadOnlyList<GccQuoteablePage> Pages,
    IReadOnlyList<string> Warnings,
    string? Refusal,
    /// <summary>
    /// The partner pages of <see cref="Pages"/> again, as typed blocks instead of flat prose.
    ///
    /// <para>
    /// Partner only, by construction rather than by a filter at the far end. A tool page's block
    /// quotation must cite a partner: a competitor is read and never quoted, and the publisher's own
    /// pages are what the piece must not repeat. One list covering all three corpora would hand the
    /// quote cutter spans it must refuse, and the only thing standing between a competitor's prose
    /// and a blockquote attributing it would be a caller remembering to filter — the same reason
    /// <see cref="CompetitorPages"/> is its own list rather than a tag on <see cref="Pages"/>.
    /// </para>
    /// <para>
    /// So the other two corpora are not mapped at all, which is also the cheaper half: the read
    /// behind this costs one repository round trip per run, and the project-site and competitor runs
    /// were paying it for a list nothing could legitimately use.
    /// </para>
    /// </summary>
    IReadOnlyList<GccGroundedPassage> PartnerPassages,
    /// <summary>
    /// Competitor pages, kept apart from <see cref="Pages"/> rather than tagged inside it.
    ///
    /// <para>
    /// A rival is read, never cited. One list would put competitor prose under a header whose rules
    /// say "name the source where the claim appears, and carry its URL", and would hand competitor
    /// pages to the partner extractor and to the <c>SoftwareApplication.url</c> derivation, both of
    /// which read the partner list wholesale. Two lists make every existing consumer correct by
    /// construction; a tag would need each of them found and filtered.
    /// </para>
    /// </summary>
    IReadOnlyList<GccQuoteablePage> CompetitorPages,
    /// <summary>
    /// Pages from the publisher's own site, kept apart again for the same reason: the instruction
    /// attached to them is a third one. Partner evidence is cited, competitor evidence is never
    /// cited, and the publisher's own pages are neither — they are what this piece must not repeat.
    /// </summary>
    IReadOnlyList<GccQuoteablePage> SitePages,
    /// <summary>
    /// Declared partners whose indexed run returned no passage for this topic, as the operator spelled
    /// them. Not a refusal here, because the outcome depends on the type: a pillar or blog obliged to
    /// name every partner is refused for it (GccGenerationCoordinator), and a tool fan-out refuses only
    /// that partner's page and writes the others.
    /// </summary>
    IReadOnlyList<string>? PartnersWithoutPassages = null,
    /// <summary>
    /// What the publisher states on their own site page, by heading, read from the site's crawl page
    /// (<see cref="GccPublisherPositionsReader"/>). Empty when the site crawl was not resolved or the
    /// page has no sections -- and then a warning says so.
    /// </summary>
    IReadOnlyList<GccPublisherPosition>? PublisherPositions = null)
{
    public bool Refused => !string.IsNullOrWhiteSpace(Refusal);

    /// <summary>Evidence was required and is unavailable. The caller must not generate.</summary>
    public static GccGroundingOutcome Refuse(string reason) => new([], [], reason, [], [], []);

    /// <summary>Nothing to cite and nothing to retrieve for this content type.</summary>
    public static GccGroundingOutcome NotRequired() => new([], [], null, [], [], []);
}

/// <summary>
/// One retrieved page as typed structure rather than flat prose.
/// </summary>
/// <remarks>
/// Retrieval returns the plaintext projection, which is correct for matching a quote to its page
/// but carries no block types. The blocks were never lost — they are on the crawl page — so this
/// reads them back and maps them kind for kind, which is what lets a retrieved quote arrive as a
/// <c>QuoteParagraph</c> carrying its source rather than as an anonymous paragraph.
/// </remarks>
public sealed record GccGroundedPassage(
    string Url,
    string Title,
    IReadOnlyList<Paragraph> Content);

/// <summary>
/// Resolves the retrieved, citable evidence a content type requires, or refuses with a named
/// reason. Retrieval only — the model writes (<c>CLAUDE.md</c> §1).
/// </summary>
/// <remarks>
/// <para>The chain is: project → its partner/competitor URLs → the crawl run indexed for each host
/// → a RAG Library query against that run. Partner and competitor URLs live on the project, which
/// is why a create without <c>ProjectId</c> cannot be grounded at all.</para>
/// <para><b>Requirements are declared per content type, and refusal is at the draft.</b> A
/// half-grounded article is the middle state this codebase bans; a social post is not refused for
/// want of a partner crawl because it never declared one.</para>
/// </remarks>
public sealed class GccGroundingResolver(
    IGccProjectReader repo,
    IGeekCrawlerRagClient rag,
    GccTypedPassageReader typedPassages,
    GccPublisherPositionsReader publisherPositions,
    ILogger<GccGroundingResolver> logger)
{
    /// <summary>
    /// What each content type must be able to cite before it may be written. A type absent from
    /// this table is never refused for missing evidence.
    /// </summary>
    /// <remarks>
    /// Pillar/Blog/TechArticle were removed 2026-09-22 (Jeff: "I told you to remove this from all
    /// content types" — citing a source was never a requirement of any content type; see the
    /// citation-purpose-and-policy memory). That was right as policy, and it had a consequence
    /// nobody intended: this table was also the retrieval trigger, so removing a type from it
    /// stopped that type fetching anything at all. Pillar and Blog spent a week obliged by
    /// <c>GccRequiredToolMentions</c> to name every declared partner while being handed nothing
    /// about any of them. The two questions are now two tables — see <see cref="RetrieveFor"/>.
    /// </remarks>
    private static readonly Dictionary<string, string[]> MustCiteCrawlTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["tool"] = [CrawlTypes.Partner],
        };

    /// <summary>
    /// What each content type goes and fetches before it is written, whether or not it must cite it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every long-form type retrieves both. Partner evidence is what makes a claim about a partner
    /// true; competitor evidence is what makes a piece differentiated, which is the stated purpose
    /// of crawling rivals at all. Neither is optional enrichment — the corpus is crawled, extracted,
    /// ingested and vector-indexed for exactly this, and until now only Tool ever asked for any of
    /// it, and nothing ever asked for a competitor.
    /// </para>
    /// <para>
    /// Retrieval failure is a refusal only where <see cref="MustCiteCrawlTypes"/> says so. A pillar
    /// whose competitor crawl is missing is a thinner pillar, not a draft that must not exist.
    /// </para>
    /// </remarks>
    private static readonly string[] EveryGroundingCrawlType =
        [CrawlTypes.ProjectSite, CrawlTypes.Partner, CrawlTypes.Competitors];

    private static readonly Dictionary<string, string[]> RetrieveCrawlTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pillar"] = EveryGroundingCrawlType,
            ["blog"] = EveryGroundingCrawlType,
            ["tool"] = EveryGroundingCrawlType,
        };

    /// <summary>
    /// One key per content type. <c>aiTool</c> is the picker's spelling of <c>tool</c>, and listing
    /// both was a second row for one type -- the shape that lets two spellings of one thing drift
    /// apart. Letters only, matching the normalisation <c>GccGenerationCoordinator</c>'s dispatch
    /// and the disabled-type check already use.
    /// </summary>
    private static string Canonical(string contentType)
    {
        var letters = new string((contentType ?? string.Empty).Where(char.IsLetter).ToArray());
        return letters.Equals("aitool", StringComparison.OrdinalIgnoreCase) ? "tool" : letters;
    }

    /// <summary>
    /// <c>local</c> is deliberately absent: it is geography for local SEO, not evidence about this
    /// topic, and nothing on these three content types reads it. Adding it would be retrieval
    /// nobody consumes.
    /// </summary>

    /// <summary>How many passages to retrieve per run. Matches the library writer's default.</summary>
    private const int TopK = 8;

    /// <summary>
    /// How many to retrieve from a <b>partner</b> run, which is a different question.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eight is right for prose: pillar and blog want the passages that best answer one topical query.
    /// A tool page wants a partner's <i>product surface</i> — features, pricing, integrations, FAQ, case
    /// studies — and <c>GccGenerateService</c> gates a page on 3 of its payload categories being
    /// populated. Eight topically-ranked pages cannot span them, and did not: a live run
    /// gave Dext 2 of 22 from 6 pages, and Bill, Melio and Stampli 1 of 22 from 5–7, against partners
    /// carrying 181–231 crawled pages with 33–55 features each (Jeff's counts, 2026-10-01).
    /// </para>
    /// <para>
    /// 32 because that is where the next ceiling already sits: <c>GccTypedPassageReader</c> reads at most
    /// 32 pages back per run (the repository's by-seeds route caps there), so retrieving more partner
    /// pages than that would hand the quote cutter spans with no typed blocks behind them.
    /// </para>
    /// </remarks>
    private const int PartnerTopK = 32;

    /// <summary>
    /// Passages per question when a partner run is asked from the brief: the core problem, then one
    /// question per evidence row. Eight each, page-diverse on the index's side, so a partner with
    /// five rows reaches up to forty-eight pages chosen for six different things rather than
    /// thirty-two chosen for one (plans/retrieval-from-the-brief.md P5). The union is deduped by URL.
    /// </summary>
    private const int EvidenceTopK = 8;


    /// <summary>
    /// The evidence <paramref name="contentType"/> must be able to cite, or empty when it declares
    /// none. Exposed so the policy itself can be asserted without a repository or a RAG client.
    /// </summary>
    internal static IReadOnlyList<string> RequiredFor(string contentType) =>
        MustCiteCrawlTypes.TryGetValue(Canonical(contentType), out var required) ? required : [];

    /// <summary>What <paramref name="contentType"/> fetches before it is written.</summary>
    internal static IReadOnlyList<string> RetrieveFor(string contentType) =>
        RetrieveCrawlTypes.TryGetValue(Canonical(contentType), out var fetch) ? fetch : [];

    /// <summary>
    /// One content type's grounding. Kept so a caller holding a single type reads naturally, and so
    /// every existing test of the refusal semantics exercises the same code the multi-type path does.
    /// </summary>
    public Task<GccGroundingOutcome> ResolveAsync(
        GccCreateDto create,
        string contentType,
        CancellationToken ct = default) =>
        ResolveAsync(create, [contentType], ct);

    /// <summary>
    /// The grounding for every content type one Generate will write, resolved once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Evidence is a property of the create, not of the draft being written from it. This used to
    /// run once per requested content type inside the coordinator's fan-out, so a pillar+blog+tool
    /// generate issued the same 21 vector queries three times and threw two of the answers away.
    /// </para>
    /// <para>
    /// It was also a correctness defect, not only a cost one. <c>seenUrls</c> is shared across crawl
    /// types, and the walk order used to be <c>mustCite.Concat(retrieveFor)</c> — which puts Partner
    /// first for <c>tool</c> and ProjectSite first for everything else. A URL present in two corpora
    /// therefore landed in a different list depending on which type was being written: partner
    /// evidence for the tool page, own-site evidence for the pillar, from one create at one moment.
    /// The order is now fixed and independent of <c>mustCite</c>, which is about refusal and has no
    /// business deciding which list a shared URL falls into.
    /// </para>
    /// <para>
    /// Refusal is unchanged in effect. A requested type that must cite evidence it cannot get still
    /// refuses the generate, and it does so here, before any type is attempted and before any model is
    /// paid: this is a refusal of the request, not of one type's draft (which
    /// <c>GccGenerationCoordinator</c> keeps apart from the types that did write). What changes is that
    /// the message names the type that required it rather than the type that happened to be running.
    /// </para>
    /// </remarks>
    public async Task<GccGroundingOutcome> ResolveAsync(
        GccCreateDto create,
        IReadOnlyList<string> contentTypes,
        CancellationToken ct = default)
    {
        var mustCite = contentTypes
            .SelectMany(RequiredFor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var retrieveFor = contentTypes
            .SelectMany(RetrieveFor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // For messages: the type that actually imposed the citation requirement, so a refusal names
        // "tool" rather than whichever type the loop was on.
        var contentType = contentTypes.FirstOrDefault(t => RequiredFor(t).Count > 0)
            ?? contentTypes.FirstOrDefault()
            ?? string.Empty;

        if (mustCite.Count == 0 && retrieveFor.Count == 0)
        {
            return GccGroundingOutcome.NotRequired();
        }

        // Anything that must be cited is also fetched, whatever the retrieve table says -- the two
        // tables answer different questions and must not be able to contradict each other.
        //
        // Order is fixed and owes nothing to mustCite, because order no longer decides anything:
        // dedupe is per crawl type (see seenByCrawlType below), so a page lands in the list for the
        // corpus it was retrieved from and nothing can move it.
        //
        // This was mustCite.Concat(retrieveFor), which made the order per content type -- tool
        // walked Partner-first, pillar ProjectSite-first -- and with a shared dedupe that meant one
        // URL landed in different lists depending on which draft was being written. Unioning
        // mustCite across the generate made it consistent and wrong in the other direction: pillar
        // and blog inherited tool's Partner-first precedence, so a URL that is the publisher's own
        // page became "partner evidence" for a pillar. Jeff, 2026-10-01: "Blog & Pillar describe
        // solutions, Tool actually Blockquote cites, and project-site is n/a" -- the three lists
        // carry three different instructions and a URL's role is decided by the corpus it came
        // from, not by what is being written from it.
        var crawlTypes = mustCite.Concat(retrieveFor).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (create.ProjectId is not Guid projectId || projectId == Guid.Empty)
        {
            if (mustCite.Count > 0)
            {
                return GccGroundingOutcome.Refuse(
                    $"'{contentType}' must cite partner or competitor evidence, and this create belongs "
                    + "to no project. The project owns those URLs; attach the create to one and retry.");
            }

            logger.LogInformation(
                "Create {CreateId} ({ContentType}) belongs to no project, so there is nothing to "
                + "retrieve. It cites nothing, so this is not a refusal.", create.Id, contentType);
            return GccGroundingOutcome.NotRequired();
        }

        var project = await repo.GetProjectAsync(projectId, ct);
        if (project is null)
        {
            if (mustCite.Count > 0)
            {
                return GccGroundingOutcome.Refuse(
                    $"'{contentType}' requires evidence from project {projectId}, which was not found.");
            }

            logger.LogWarning(
                "Project {ProjectId} was not found, so create {CreateId} retrieves nothing.",
                projectId, create.Id);
            return GccGroundingOutcome.NotRequired();
        }

        // Host -> partner spelling for this project, built once. GccRequiredToolMentions owns the
        // precedence (a brief row's spelling beats a host-derived one), so a chunk labelled from its
        // links and the required-mentions block in the prompt can never name one partner two ways.
        // The brief comes off the create, not the project: GccProjectDto carries PartnerUrls but no
        // brief (GccProjectDtos.cs:18-37), and partner URLs alone can only yield host-derived names
        // -- "Zoneandco" where the operator wrote "Zone & Co". Both halves are what makes the
        // spelling authoritative.
        var anchorToolLookup = GccRequiredToolMentions.AnchorLookup(create.BriefJson, project.PartnerUrls);

        var retrieved = new List<GccQuoteablePage>();
        var competitors = new List<GccQuoteablePage>();
        var sitePages = new List<GccQuoteablePage>();
        IReadOnlyList<GccPublisherPosition> positions = [];
        var passages = new List<GccGroundedPassage>();
        var warnings = new List<string>();
        // Per crawl type, not across them. The same URL retrieved from two runs of one corpus is one
        // page and is deduped; the same URL present in two different corpora is a declaration the
        // operator made twice, and silently assigning it to whichever was walked first is how the
        // role became order-dependent. Each list means something different -- cite / differentiate
        // from / do not repeat -- so a page is in a list because of where it came from.
        var seenByCrawlType = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var partnersWithoutPassages = new List<string>();
        // Partner run -> the declared hosts it was indexed for, so a run that returns nothing can be
        // refused under the partner's name rather than a run id the operator never sees.
        var partnerHostsByRun = new Dictionary<Guid, List<string>>();
        // Partner run -> the host key (bill.com) the brief's per-tool framing is filed under.
        var partnerHostKeyByRun = new Dictionary<Guid, string>();

        foreach (var crawlType in crawlTypes)
        {
            // Binary, and the question is per URL: does an indexed crawl exist for it?
            //
            // Every declared partner and competitor URL is index-checked before a project may be
            // saved, and the project site is a run id the form resolves from the index and refuses
            // to create a project without. So reaching generation, the answer is already yes for
            // everything declared. A no here is a dropped crawl or a project that pre-dates the
            // gate -- a fault, and it stops.
            //
            // A type the project declares none of asks no question, so there is nothing to fail. An
            // empty list is an empty list, here as at declare time. Whether a tool page with no
            // partner may exist is a different question, asked and answered downstream by
            // GenerateToolPageAsync's own partner-grounding refusal.
            var urls = crawlType switch
            {
                CrawlTypes.Partner => project.PartnerUrls,
                CrawlTypes.Competitors => project.CompetitorUrls,
                _ => [],
            };

            List<Guid> runIds;
            // What the operator entered for each run, so every message names a URL they recognise
            // rather than a run id that means nothing to them.
            var namesByRun = new Dictionary<Guid, string>();
            if (string.Equals(crawlType, CrawlTypes.ProjectSite, StringComparison.OrdinalIgnoreCase))
            {
                if (project.ProjectSiteRunId is not { } siteRun || siteRun == Guid.Empty)
                {
                    return GccGroundingOutcome.Refuse(
                        $"Project '{project.Name}' has no project-site crawl run. Crawl and index "
                        + "the site, then retry.");
                }

                runIds = [siteRun];
                namesByRun[siteRun] = string.IsNullOrWhiteSpace(project.SiteUrl) ? project.Name : project.SiteUrl;

                // The publisher's own positions, read from the site page itself -- not retrieved by the
                // keyword question below, which is why they were never in front of the writer before.
                if (!string.IsNullOrWhiteSpace(project.SiteUrl))
                {
                    var (read, warning) = await publisherPositions.ReadAsync(siteRun, project.SiteUrl, ct);
                    positions = read;
                    if (warning is not null) warnings.Add(warning);
                }
                else
                {
                    warnings.Add($"Project '{project.Name}' has no site URL, so the writer has none of the publisher's own positions.");
                }
            }
            else
            {
                if (urls.Count == 0) continue;

                var indexed = await rag.HostsIndexedAsync(urls, crawlType, ct);

                // An empty answer is the client's unreachable shape, not "nothing is indexed" -- the
                // declare-time check reads it the same way. Named as an outage so the operator is not
                // sent to re-crawl partners that are fine.
                if (indexed.Count == 0)
                {
                    return GccGroundingOutcome.Refuse(
                        $"The index could not be reached to check project '{project.Name}'s "
                        + $"{urls.Count} {crawlType} URL(s). Nothing was generated -- retry.");
                }

                // Every declared URL, not "at least one". This refused only when all of them were
                // unindexed, so one dropped partner crawl out of five was skipped without a word and
                // the draft was written about four partners while the prompt named five -- against
                // the comment above, which has always said a no here is a fault and it stops.
                //
                // A URL the index never answered for counts as unindexed: silence about a declared
                // URL is not evidence that it is crawled.
                var answered = indexed
                    .Where(host => host.Indexed && Guid.TryParse(host.RunId, out var id) && id != Guid.Empty)
                    .ToList();
                var unindexed = urls
                    .Where(url => !answered.Any(host => string.Equals(host.Url, url, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (unindexed.Count > 0)
                {
                    return GccGroundingOutcome.Refuse(
                        $"{unindexed.Count} of project '{project.Name}'s {urls.Count} {crawlType} URL(s) "
                        + $"has no indexed crawl: {string.Join(", ", unindexed)}. Every declared URL is "
                        + "index-checked before a project is saved, so this is a dropped crawl. "
                        + "Re-crawl and index it, then retry.");
                }

                runIds = answered
                    .Select(host => Guid.Parse(host.RunId!))
                    .Distinct()
                    .ToList();
                foreach (var group in answered.GroupBy(host => Guid.Parse(host.RunId!)))
                    namesByRun[group.Key] = string.Join(", ", group.Select(host => host.Url));

                if (string.Equals(crawlType, CrawlTypes.Partner, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var host in answered)
                    {
                        var runId = Guid.Parse(host.RunId!);
                        if (!partnerHostsByRun.TryGetValue(runId, out var hosts))
                        {
                            hosts = [];
                            partnerHostsByRun[runId] = hosts;
                        }

                        var label = PartnerLabel(host, anchorToolLookup);
                        if (!hosts.Contains(label, StringComparer.OrdinalIgnoreCase)) hosts.Add(label);

                        // The brief's per-tool framing is keyed by host (bill.com), so the run's
                        // questions are looked up by the host that resolved to it. One host per run.
                        partnerHostKeyByRun.TryAdd(runId, GccRequiredToolMentions.HostKeyOf(host.Url));
                    }
                }
            }

            foreach (var runId in runIds)
            {
                // Which list a page lands in is decided here, by the crawl type that was queried,
                // and nowhere else. It is the only point where that is known: the query result does
                // not carry it back and no field on the page records it.
                var into = crawlType switch
                {
                    CrawlTypes.Competitors => competitors,
                    CrawlTypes.ProjectSite => sitePages,
                    _ => retrieved,
                };

                if (!seenByCrawlType.TryGetValue(crawlType, out var seenUrls))
                {
                    seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    seenByCrawlType[crawlType] = seenUrls;
                }

                // A partner run is asked from the brief: its core problem, then one question per
                // evidence row, each with the vendor's solution for the meaning half and the row's
                // terms for the keyword half. Without a brief it is asked the keyword alone. The
                // other corpora are asked their one question as before.
                var isPartner = string.Equals(crawlType, CrawlTypes.Partner, StringComparison.OrdinalIgnoreCase);
                var questions = isPartner
                    ? PartnerQuestions(create, partnerHostKeyByRun.GetValueOrDefault(runId))
                    : [new PartnerQuestion(BuildNeed(create.Topic, crawlType), null, TopK)];

                var pagesReturned = 0;
                foreach (var question in questions)
                {
                    var result = await rag.QueryAsync(
                        new GeekCrawlerRagQuery(
                            question.Need,
                            runId,
                            CrawlType: crawlType,
                            TopK: question.TopK,
                            Keyword: question.Keyword,
                            AnchorToolLookup: anchorToolLookup),
                        ct);

                    // The library failing is the library failing, whatever the content type. Empty
                    // Pages on a successful query is judged per run, below, once every question is in.
                    if (result is null)
                    {
                        return GccGroundingOutcome.Refuse(
                            $"The evidence library returned nothing for {CrawlTypeLabel(crawlType).ToLowerInvariant()} {namesByRun[runId]}. "
                            + $"'{contentType}' cannot be grounded.");
                    }

                    if (result.Failed)
                    {
                        return GccGroundingOutcome.Refuse(
                            result.Error ?? result.Warning
                            ?? $"The evidence library query failed for {CrawlTypeLabel(crawlType).ToLowerInvariant()} {namesByRun[runId]}.");
                    }

                    pagesReturned += result.Pages.Count;
                    if (!string.IsNullOrWhiteSpace(result.Warning) && result.Pages.Count > 0)
                    {
                        var line = $"{CrawlTypeLabel(crawlType)} {namesByRun[runId]}: {result.Warning}";
                        if (!warnings.Contains(line, StringComparer.Ordinal)) warnings.Add(line);
                    }

                    var fresh = new List<GccQuoteablePage>();
                    foreach (var page in result.Pages)
                    {
                        if (!seenUrls.Add(page.Url)) continue;
                        into.Add(page);
                        fresh.Add(page);
                    }

                    // Partner only -- see PartnerPassages. The competitor and project-site runs skip
                    // this read entirely rather than mapping blocks no consumer may quote from.
                    if (isPartner && fresh.Count > 0)
                    {
                        passages.AddRange(await typedPassages.ReadAsync(runId, fresh, ct));
                    }
                }

                if (pagesReturned == 0)
                {
                    // RAG's own warning carries the run id ("No chunks for runId=..."); the operator
                    // gets the URL they entered and what it means for the draft instead.
                    warnings.Add(
                        $"{CrawlTypeLabel(crawlType)} {namesByRun[runId]}: the index finds nothing from its "
                        + "crawl, so this was written without it");
                }

                // Every declared partner must return evidence -- but what its absence refuses depends on
                // the type, so it is recorded here and decided by the caller. Pillar and Blog are
                // obliged by GccRequiredToolMentions to name every declared partner, so a partner with
                // no passage is one the draft must write about from nothing: those types are refused,
                // naming it. A tool fan-out writes one page per partner, and refuses that partner's
                // page alone -- four pages and one named refusal, not none. Competitor and own-site
                // runs keep the old rule: those corpora inform the piece, and another run may cover
                // what this one did not.
                if (isPartner && pagesReturned == 0)
                {
                    partnersWithoutPassages.Add(partnerHostsByRun.TryGetValue(runId, out var hosts)
                        ? string.Join(", ", hosts)
                        : namesByRun[runId]);
                }
            }
        }

        if (mustCite.Count > 0 && retrieved.Count == 0)
        {
            return GccGroundingOutcome.Refuse(
                $"'{contentType}' must cite {string.Join(" and ", mustCite)} evidence. Every "
                + "indexed run was queried and none returned a citable passage for this topic.");
        }

        // A type that must cite a partner quotes from the typed blocks, not from the flat
        // projection, so for that type the typed read is the evidence rather than a nicer shape for
        // it. Empty here with partner pages present means the crawl pages behind them could not be
        // read back or carried no mappable blocks -- the quote has no source, and the draft must
        // stop now, under the reason that is actually true.
        //
        // This is the one case GccTypedPassageReader's empty result is not merely an enrichment
        // missing, and it is right everywhere else: pillar and blog never quote, so for them the
        // typed form stays an enrichment and its absence changes nothing. Without this,
        // a repository outage reached the writer and came back as "the tool page does not carry a
        // verifiable block quotation" -- a refusal that blames the model for an infrastructure
        // failure and sends the operator to the prompt to fix it.
        if (mustCite.Contains(CrawlTypes.Partner, StringComparer.OrdinalIgnoreCase)
            && retrieved.Count > 0
            && passages.Count == 0)
        {
            return GccGroundingOutcome.Refuse(
                $"'{contentType}' must quote partner evidence, and none of the {retrieved.Count} "
                + "retrieved partner page(s) could be read back as typed blocks. The pages are "
                + "indexed but their crawl rows are unreadable or carry no blocks -- re-crawl them, "
                + "then retry.");
        }

        logger.LogInformation(
            "Grounding resolved for create {CreateId} ({ContentType}): {PageCount} partner, "
            + "{CompetitorCount} competitor, {SiteCount} own-site pages, {PositionCount} publisher positions; "
            + "{PassageCount} typed partner passages, {WarningCount} warnings.",
            create.Id, contentType, retrieved.Count, competitors.Count, sitePages.Count, positions.Count,
            passages.Count, warnings.Count);

        return new GccGroundingOutcome(
            retrieved, warnings, null, passages, competitors, sitePages, partnersWithoutPassages, positions);
    }

    /// <summary>How a crawl type reads in a message to the operator.</summary>
    private static string CrawlTypeLabel(string crawlType) => crawlType switch
    {
        CrawlTypes.Competitors => "Competitor",
        CrawlTypes.Partner => "Partner",
        CrawlTypes.ProjectSite => "Site",
        _ => crawlType,
    };

    /// <summary>
    /// The retrieval query. What comes back is what a quotation can be chosen from, so this decides
    /// whether the right span is in the pool at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a partner or the project site it is the keyword from the topic (<see cref="GccTopic"/>:
    /// "descriptor: keyword" yields the keyword; a topic with no colon is the keyword), and nothing
    /// else. The index runs a hybrid search -- meaning and keyword halves fused -- so every word
    /// sent is a term the keyword half matches.
    /// </para>
    /// <para>
    /// From 2026-10-02 to 2026-10-08 twenty-two fixed words were appended ("the cost, delay and
    /// error rate of the manual or status-quo way, the capability that removes it, and measured
    /// outcomes"), written for a retrieval that was then meaning-only, as a stand-in for the
    /// problem the operator describes in the brief's niche framing -- which this method never
    /// read. Once the keyword half ran, those words matched every ERP, pricing and integration
    /// page: on Melio they put an accounts-receivable article in the second slot. Deleted at
    /// Jeff's instruction (2026-10-08). The brief's own framing is still not read here; that is a
    /// separate decision.
    /// </para>
    /// </remarks>
    internal static string BuildNeed(string topic, string crawlType)
    {
        if (crawlType == CrawlTypes.Competitors)
        {
            return $"competitor differentiation research; topic: {Bounded(topic, 200)}";
        }

        return Bounded(GccTopic.KeywordOf(topic), 150);
    }

    /// <summary>One search of a partner run: the meaning half's text, the keyword half's, and how many passages.</summary>
    internal sealed record PartnerQuestion(string Need, string? Keyword, int TopK);

    /// <summary>
    /// What a partner run is asked, from the brief's niche framing for that host: the core problem
    /// (the tool's own, else the category's), then one question per evidence row -- the vendor's
    /// solution for the meaning half, the row's terms for the keyword half. A brief with neither
    /// falls back to the keyword alone at <see cref="PartnerTopK"/>, which is what every partner was
    /// asked before 2026-10-08.
    /// </summary>
    /// <remarks>
    /// Measured on Tipalti the day this was written: the keyword alone returned product and ERP
    /// integration pages; the tool's core problem returned case studies, pricing and solution pages;
    /// solution descriptions in the vendor's own vocabulary found the product page for all six of
    /// the operator's failures, including the three the pain points missed.
    /// </remarks>
    internal static IReadOnlyList<PartnerQuestion> PartnerQuestions(GccCreateDto create, string? hostKey)
    {
        var framing = string.IsNullOrWhiteSpace(hostKey)
            ? null
            : GccNicheFramingReader.ForHost(create.BriefJson, hostKey);

        var questions = new List<PartnerQuestion>();
        if (framing is not null && framing.CoreProblem.Length > 0)
        {
            questions.Add(new PartnerQuestion(framing.CoreProblem, null, EvidenceTopK));
        }

        foreach (var row in framing?.Evidence ?? [])
        {
            if (row.Need.Length == 0) continue;
            questions.Add(new PartnerQuestion(row.Need, row.Keyword, EvidenceTopK));
        }

        if (questions.Count == 0)
        {
            questions.Add(new PartnerQuestion(BuildNeed(create.Topic, CrawlTypes.Partner), null, PartnerTopK));
        }

        return questions;
    }

    /// <summary>
    /// The partner as the operator spelled it, with its host, so a refusal names something the
    /// operator declared. The same lookup the retrieval labels chunks with -- one spelling per partner.
    /// </summary>
    private static string PartnerLabel(
        GeekCrawlerRagHostIndex host, IReadOnlyDictionary<string, string> anchorToolLookup)
    {
        var hostName = string.IsNullOrWhiteSpace(host.Host) ? host.Url : host.Host;
        return hostName is not null
            && anchorToolLookup.TryGetValue(hostName, out var spelled)
            && !string.IsNullOrWhiteSpace(spelled)
            ? $"{spelled} ({hostName})"
            : hostName ?? host.Url;
    }

    private static string Bounded(string topic, int max)
    {
        var trimmed = topic.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
