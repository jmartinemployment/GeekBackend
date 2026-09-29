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

    public async Task<GccGroundingOutcome> ResolveAsync(
        GccCreateDto create,
        string contentType,
        CancellationToken ct = default)
    {
        var mustCite = RequiredFor(contentType);
        var retrieveFor = RetrieveFor(contentType);
        if (mustCite.Count == 0 && retrieveFor.Count == 0)
        {
            return GccGroundingOutcome.NotRequired();
        }

        // Anything that must be cited is also fetched, whatever the retrieve table says -- the two
        // tables answer different questions and must not be able to contradict each other.
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
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var crawlType in crawlTypes)
        {
            // Whether this crawl type's absence refuses the draft or only thins it.
            var cited = mustCite.Contains(crawlType, StringComparer.OrdinalIgnoreCase);

            var urls = crawlType switch
            {
                CrawlTypes.Partner => project.PartnerUrls,
                CrawlTypes.Competitors => project.CompetitorUrls,
                _ => [],
            };

            // The project site is resolved by the run id the project already carries. It is the one
            // crawl type that needs no host lookup: ProjectForm resolves that id from the index at
            // declare time and refuses to create a project without it.
            List<Guid> runIds;
            if (string.Equals(crawlType, CrawlTypes.ProjectSite, StringComparison.OrdinalIgnoreCase))
            {
                if (project.ProjectSiteRunId is not { } siteRun || siteRun == Guid.Empty)
                {
                    // Pre-dates the gate, or the crawl was dropped. Either way it is a fault, not a
                    // state to write around.
                    return GccGroundingOutcome.Refuse(
                        $"Project '{project.Name}' has no project-site crawl run. Its own pages are "
                        + "what stops this piece repeating what the site already says. Crawl and "
                        + "index the site, then retry.");
                }

                runIds = [siteRun];
            }
            else
            {
                if (urls.Count == 0)
                {
                    // A type this content type must cite is a different matter: a tool page with no
                    // partner declared has no subject, not a thinner one.
                    if (cited)
                    {
                        return GccGroundingOutcome.Refuse(
                            $"'{contentType}' must cite {crawlType} evidence, and project "
                            + $"'{project.Name}' has no {crawlType} URLs.");
                    }

                    // Otherwise not a fault -- an empty list blocks nothing, here as at declare time.
                    continue;
                }

                var indexed = await rag.HostsIndexedAsync(urls, ct);
                runIds = indexed
                    .Where(host => host.Indexed)
                    .Select(host => Guid.TryParse(host.RunId, out var id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();

                if (runIds.Count == 0)
                {
                    // Declared URLs are index-checked before a project may be saved, so reaching
                    // here means the crawl was dropped or the project pre-dates that gate. Not a
                    // thin draft -- a fault, reported as one.
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

                // A null client result and Failed are both failures. Empty Pages on a successful
                // query is not — it means this run had nothing relevant, which other runs may cover.
                if (result is null)
                {
                    if (cited)
                    {
                        return GccGroundingOutcome.Refuse(
                            $"The evidence library returned nothing for {crawlType} run {runId}. "
                            + $"'{contentType}' cannot be grounded.");
                    }

                    warnings.Add($"The evidence library returned nothing for {crawlType} run {runId}.");
                    continue;
                }

                if (result.Failed)
                {
                    var reason = result.Error ?? result.Warning
                        ?? $"The evidence library query failed for {crawlType} run {runId}.";
                    if (cited)
                    {
                        return GccGroundingOutcome.Refuse(reason);
                    }

                    warnings.Add(reason);
                    continue;
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
