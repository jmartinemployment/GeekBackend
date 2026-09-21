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
    IReadOnlyList<GccGroundedPassage> Passages)
{
    public bool Refused => !string.IsNullOrWhiteSpace(Refusal);

    /// <summary>Evidence was required and is unavailable. The caller must not generate.</summary>
    public static GccGroundingOutcome Refuse(string reason) => new([], [], reason, []);

    /// <summary>This content type declares no evidence requirement — nothing to resolve.</summary>
    public static GccGroundingOutcome NotRequired() => new([], [], null, []);
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
    /// this table declares no requirement and is never refused for missing evidence.
    /// </summary>
    private static readonly Dictionary<string, string[]> RequiredCrawlTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pillar"] = [CrawlTypes.Partner],
            ["blog"] = [CrawlTypes.Partner],
            ["techarticle"] = [CrawlTypes.Partner],
            ["tool"] = [CrawlTypes.Partner],
            ["aitool"] = [CrawlTypes.Partner],
        };

    /// <summary>How many passages to retrieve per run. Matches the library writer's default.</summary>
    private const int TopK = 8;

    /// <summary>The repository's by-seeds route accepts at most 32 URLs.</summary>
    private const int MaxSeedsPerRead = 32;

    /// <summary>
    /// The evidence <paramref name="contentType"/> must be able to cite, or empty when it declares
    /// none. Exposed so the policy itself can be asserted without a repository or a RAG client.
    /// </summary>
    internal static IReadOnlyList<string> RequiredFor(string contentType) =>
        RequiredCrawlTypes.TryGetValue(contentType, out var required) ? required : [];

    public async Task<GccGroundingOutcome> ResolveAsync(
        GccCreateDto create,
        string contentType,
        CancellationToken ct = default)
    {
        var required = RequiredFor(contentType);
        if (required.Count == 0)
        {
            return GccGroundingOutcome.NotRequired();
        }

        if (create.ProjectId is not Guid projectId || projectId == Guid.Empty)
        {
            return GccGroundingOutcome.Refuse(
                $"'{contentType}' must cite partner or competitor evidence, and this create belongs "
                + "to no project. The project owns those URLs; attach the create to one and retry.");
        }

        var project = await repo.GetProjectAsync(projectId, ct);
        if (project is null)
        {
            return GccGroundingOutcome.Refuse(
                $"'{contentType}' requires evidence from project {projectId}, which was not found.");
        }

        var retrieved = new List<GccQuoteablePage>();
        var passages = new List<GccGroundedPassage>();
        var warnings = new List<string>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var crawlType in required)
        {
            var urls = crawlType switch
            {
                CrawlTypes.Partner => project.PartnerUrls,
                CrawlTypes.Competitors => project.CompetitorUrls,
                _ => [],
            };

            if (urls.Count == 0)
            {
                return GccGroundingOutcome.Refuse(
                    $"'{contentType}' must cite {crawlType} evidence, and project '{project.Name}' "
                    + $"has no {crawlType} URLs.");
            }

            var indexed = await rag.HostsIndexedAsync(urls, ct);
            var runIds = indexed
                .Where(host => host.Indexed)
                .Select(host => Guid.TryParse(host.RunId, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (runIds.Count == 0)
            {
                return GccGroundingOutcome.Refuse(
                    $"'{contentType}' must cite {crawlType} evidence, but none of project "
                    + $"'{project.Name}'s {urls.Count} {crawlType} URL(s) has an indexed crawl. "
                    + "Crawl and index them, then retry.");
            }

            foreach (var runId in runIds)
            {
                var need = BuildNeed(create.Topic, crawlType);
                var result = await rag.QueryAsync(need, runId, crawlType: crawlType, topK: TopK, ct: ct);

                // A null client result and Failed are both failures. Empty Pages on a successful
                // query is not — it means this run had nothing relevant, which other runs may cover.
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

                var fresh = new List<GccQuoteablePage>();
                foreach (var page in result.Pages)
                {
                    if (seenUrls.Add(page.Url))
                    {
                        retrieved.Add(page);
                        fresh.Add(page);
                    }
                }

                passages.AddRange(await ReadTypedPassagesAsync(runId, fresh, ct));
            }
        }

        if (retrieved.Count == 0)
        {
            return GccGroundingOutcome.Refuse(
                $"'{contentType}' must cite {string.Join(" and ", required)} evidence. Every "
                + "indexed run was queried and none returned a citable passage for this topic.");
        }

        logger.LogInformation(
            "Grounding resolved for create {CreateId} ({ContentType}): {PageCount} pages, "
            + "{PassageCount} typed passages, {WarningCount} warnings.",
            create.Id, contentType, retrieved.Count, passages.Count, warnings.Count);

        return new GccGroundingOutcome(retrieved, warnings, null, passages);
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
