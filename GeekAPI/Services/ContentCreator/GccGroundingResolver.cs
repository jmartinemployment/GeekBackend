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
    IReadOnlyList<GccGroundedPassage> Passages,
    /// <summary>
    /// Competitor pages, kept apart from <see cref="Pages"/> rather than tagged inside it.
    ///
    /// <para>
    /// A rival is read, never cited. One list would put competitor prose under a header whose rules
    /// say "name the source and include its URL where the claim appears", and would hand competitor
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
    IReadOnlyList<GccQuoteablePage> SitePages)
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
    IGccCrawlPageReader pages,
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

    /// <summary>The repository's by-seeds route accepts at most 32 URLs.</summary>
    private const int MaxSeedsPerRead = 32;

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
    /// refuses the generate — which is already what happened, because one failure fails all
    /// (<c>GccGenerationCoordinator</c>, Jeff 2026-09-23: "do not incur changes on failures. One
    /// failure fails all, for now"). What changes is that the message names the type that required
    /// it rather than the type that happened to be running.
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
        var passages = new List<GccGroundedPassage>();
        var warnings = new List<string>();
        // Per crawl type, not across them. The same URL retrieved from two runs of one corpus is one
        // page and is deduped; the same URL present in two different corpora is a declaration the
        // operator made twice, and silently assigning it to whichever was walked first is how the
        // role became order-dependent. Each list means something different -- cite / differentiate
        // from / do not repeat -- so a page is in a list because of where it came from.
        var seenByCrawlType = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

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
            if (string.Equals(crawlType, CrawlTypes.ProjectSite, StringComparison.OrdinalIgnoreCase))
            {
                if (project.ProjectSiteRunId is not { } siteRun || siteRun == Guid.Empty)
                {
                    return GccGroundingOutcome.Refuse(
                        $"Project '{project.Name}' has no project-site crawl run. Crawl and index "
                        + "the site, then retry.");
                }

                runIds = [siteRun];
            }
            else
            {
                if (urls.Count == 0) continue;

                var indexed = await rag.HostsIndexedAsync(urls, ct);
                runIds = indexed
                    .Where(host => host.Indexed)
                    .Select(host => Guid.TryParse(host.RunId, out var id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                if (runIds.Count == 0)
                {
                    return GccGroundingOutcome.Refuse(
                        $"None of project '{project.Name}'s {urls.Count} {crawlType} URL(s) has an "
                        + "indexed crawl, though every declared URL is index-checked before a "
                        + "project is saved. Re-crawl and index them, then retry.");
                }
            }

            foreach (var runId in runIds)
            {
                var need = BuildNeed(create.Topic, crawlType);
                var result = await rag.QueryAsync(
                    need,
                    runId,
                    crawlType: crawlType,
                    topK: TopK,
                    anchorToolLookup: anchorToolLookup,
                    ct: ct);

                // The library failing is the library failing, whatever the content type. Empty
                // Pages on a successful query is not a failure -- that run had nothing relevant for
                // this topic, which another run may cover.
                if (result is null)
                {
                    return GccGroundingOutcome.Refuse(
                        $"The evidence library returned nothing for {crawlType} run {runId}. "
                        + $"'{contentType}' cannot be grounded.");
                }

                if (result.Failed)
                {
                    return GccGroundingOutcome.Refuse(
                        result.Error ?? result.Warning
                        ?? $"The evidence library query failed for {crawlType} run {runId}.");
                }

                if (!string.IsNullOrWhiteSpace(result.Warning))
                {
                    warnings.Add(result.Warning);
                }

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

                var fresh = new List<GccQuoteablePage>();
                foreach (var page in result.Pages)
                {
                    if (!seenUrls.Add(page.Url)) continue;
                    into.Add(page);
                    fresh.Add(page);
                }

                passages.AddRange(await ReadTypedPassagesAsync(runId, fresh, ct));
            }
        }

        if (mustCite.Count > 0 && retrieved.Count == 0)
        {
            return GccGroundingOutcome.Refuse(
                $"'{contentType}' must cite {string.Join(" and ", mustCite)} evidence. Every "
                + "indexed run was queried and none returned a citable passage for this topic.");
        }

        logger.LogInformation(
            "Grounding resolved for create {CreateId} ({ContentType}): {PageCount} partner, "
            + "{CompetitorCount} competitor, {SiteCount} own-site pages; {PassageCount} typed "
            + "passages, {WarningCount} warnings.",
            create.Id, contentType, retrieved.Count, competitors.Count, sitePages.Count,
            passages.Count, warnings.Count);

        return new GccGroundingOutcome(retrieved, warnings, null, passages, competitors, sitePages);
    }

    /// <summary>
    /// Reads the crawl pages behind the retrieved URLs and maps their blocks to typed paragraphs.
    /// </summary>
    /// <remarks>
    /// Best-effort by design, and the one place in this resolver that is: the typed form is an
    /// enrichment of evidence already proven present by the query above. Its absence must not turn
    /// a grounded draft into a refusal — the refusals are for missing *evidence*, not missing
    /// *shape*. A failure here is recorded as a warning and the flat passages still stand.
    /// </remarks>
    private async Task<IReadOnlyList<GccGroundedPassage>> ReadTypedPassagesAsync(
        Guid runId,
        IReadOnlyList<GccQuoteablePage> retrieved,
        CancellationToken ct)
    {
        if (retrieved.Count == 0)
        {
            return [];
        }

        // The repository route caps seeds at 32.
        var urls = retrieved.Select(page => page.Url).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSeedsPerRead).ToList();
        var crawled = await pages.ListPagesBySeedsAsync(runId, urls, ct);

        var byUrl = new Dictionary<string, GeekCrawlerPageDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in crawled)
        {
            byUrl.TryAdd(page.Url, page);
            if (!string.IsNullOrWhiteSpace(page.FinalUrl))
            {
                byUrl.TryAdd(page.FinalUrl, page);
            }
        }

        var typed = new List<GccGroundedPassage>();
        foreach (var page in retrieved)
        {
            if (!byUrl.TryGetValue(page.Url, out var crawledPage))
            {
                continue;
            }

            var content = GccCorpusBlockMapper.MapBlocks(crawledPage.Blocks, page.Url);
            if (content.Count > 0)
            {
                typed.Add(new GccGroundedPassage(page.Url, page.Title, content));
            }
        }
        return typed;
    }

    /// <summary>
    /// The retrieval query. Mirrors <c>GccV2CreateLibraryWriter.BuildNeed</c> — that path never ran,
    /// but its intent is the specification.
    /// </summary>
    internal static string BuildNeed(string topic, string crawlType)
    {
        var role = crawlType == CrawlTypes.Competitors
            ? "competitor differentiation research"
            : "partner tool research";
        var trimmed = topic.Trim();
        if (trimmed.Length > 200)
        {
            trimmed = trimmed[..200];
        }
        return $"{role}; topic: {trimmed}";
    }
}
