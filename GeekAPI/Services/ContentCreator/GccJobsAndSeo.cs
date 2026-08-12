using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Site Analyzer client for Content Creator.
/// Starts and polls a dedicated analysis; failures are returned as errors.
/// </summary>
public class HttpGeekSeoSiteAnalyzerClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpGeekSeoSiteAnalyzerClient> _logger;
    private readonly bool _enabled;

    public HttpGeekSeoSiteAnalyzerClient(HttpClient http, ILogger<HttpGeekSeoSiteAnalyzerClient> logger)
    {
        _http = http;
        _logger = logger;
        _enabled = _http.BaseAddress is not null;
    }

    public bool IsEnabled => _enabled;

    public async Task<SeoCallResult<SeoProjectDto>> EnsureProjectForDomainAsync(
        string domain,
        string? bearerToken,
        CancellationToken ct)
    {
        if (!_enabled)
        {
            return SeoCallResult<SeoProjectDto>.Fail(
                (int)HttpStatusCode.ServiceUnavailable,
                "Site Analyzer is not configured on GeekAPI (GEEK_SEO_API_URL).");
        }

        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            return SeoCallResult<SeoProjectDto>.Fail(
                (int)HttpStatusCode.Unauthorized,
                "Signed-in user required to run site analysis.");
        }

        var host = NormalizeHost(domain);
        if (string.IsNullOrWhiteSpace(host))
        {
            return SeoCallResult<SeoProjectDto>.Fail(
                (int)HttpStatusCode.BadRequest,
                "domain required");
        }

        var projectsRes = await SendAsync(HttpMethod.Get, "api/seo/projects", bearerToken, ct);
        if (!projectsRes.Ok)
            return SeoCallResult<SeoProjectDto>.Fail(projectsRes.StatusCode, projectsRes.Error!);

        var projects = JsonSerializer.Deserialize<List<SeoProjectDto>>(projectsRes.Body!, JsonOpts) ?? [];
        var project = projects.FirstOrDefault(p => HostsMatch(host, NormalizeHost(p.Url)))
            ?? projects.FirstOrDefault(p => HostsMatch(host, NormalizeHost(p.Name)));

        if (project is not null)
            return SeoCallResult<SeoProjectDto>.Success(project);

        var createRes = await SendAsync(
            HttpMethod.Post,
            "api/seo/projects",
            bearerToken,
            ct,
            new { name = host, url = $"https://{host}", defaultLocation = "United States" });
        if (!createRes.Ok)
            return SeoCallResult<SeoProjectDto>.Fail(createRes.StatusCode, createRes.Error!);

        project = JsonSerializer.Deserialize<SeoProjectDto>(createRes.Body!, JsonOpts);
        return project is null || project.Id == Guid.Empty
            ? SeoCallResult<SeoProjectDto>.Fail((int)HttpStatusCode.BadGateway, "Site Analyzer returned an invalid project.")
            : SeoCallResult<SeoProjectDto>.Success(project);
    }

    public async Task<SeoCallResult<Guid>> StartSiteAnalysisAsync(
        Guid projectId,
        string domain,
        string? seedTopic,
        string? bearerToken,
        CancellationToken ct)
    {
        if (!_enabled)
            return SeoCallResult<Guid>.Fail((int)HttpStatusCode.ServiceUnavailable, "Site Analyzer is not configured on GeekAPI (GEEK_SEO_API_URL).");
        if (string.IsNullOrWhiteSpace(bearerToken))
            return SeoCallResult<Guid>.Fail((int)HttpStatusCode.Unauthorized, "Signed-in user required to run site analysis.");

        var result = await SendAsync(
            HttpMethod.Post,
            "api/seo/site-analyzer/analyze-and-run",
            bearerToken,
            ct,
            new { projectId, domain = NormalizeHost(domain), seedTopic });
        if (!result.Ok) return SeoCallResult<Guid>.Fail(result.StatusCode, result.Error!);

        var profile = JsonSerializer.Deserialize<SeoProfileDto>(result.Body!, JsonOpts);
        if (profile?.Id is Guid id && id != Guid.Empty) return SeoCallResult<Guid>.Success(id);
        using var doc = JsonDocument.Parse(result.Body!);
        if (doc.RootElement.TryGetProperty("profileId", out var profileId)
            && Guid.TryParse(profileId.GetString(), out id))
            return SeoCallResult<Guid>.Success(id);
        return SeoCallResult<Guid>.Fail((int)HttpStatusCode.BadGateway, "Site Analyzer returned no profile ID.");
    }

    public async Task<SeoCallResult<SiteAnalysisStatus>> GetSiteAnalysisStatusAsync(
        Guid profileId,
        string? bearerToken,
        CancellationToken ct)
    {
        if (!_enabled)
            return SeoCallResult<SiteAnalysisStatus>.Fail((int)HttpStatusCode.ServiceUnavailable, "Site Analyzer is not configured on GeekAPI (GEEK_SEO_API_URL).");
        if (string.IsNullOrWhiteSpace(bearerToken))
            return SeoCallResult<SiteAnalysisStatus>.Fail((int)HttpStatusCode.Unauthorized, "Signed-in user required to load site analysis.");

        var result = await SendAsync(HttpMethod.Get, $"api/seo/site-analyzer/{profileId}/status", bearerToken, ct);
        if (!result.Ok) return SeoCallResult<SiteAnalysisStatus>.Fail(result.StatusCode, result.Error!);
        var status = JsonSerializer.Deserialize<SiteAnalysisStatus>(result.Body!, JsonOpts);
        return status is null
            ? SeoCallResult<SiteAnalysisStatus>.Fail((int)HttpStatusCode.BadGateway, "Site Analyzer returned an invalid status.")
            : SeoCallResult<SiteAnalysisStatus>.Success(status);
    }

    public async Task<SeoCallResult<SiteModelSnapshot>> LoadSiteModelByProfileAsync(
        Guid projectId,
        Guid profileId,
        string domain,
        string? bearerToken,
        CancellationToken ct)
    {
        if (!_enabled)
            return SeoCallResult<SiteModelSnapshot>.Fail((int)HttpStatusCode.ServiceUnavailable, "Site Analyzer is not configured on GeekAPI (GEEK_SEO_API_URL).");
        if (string.IsNullOrWhiteSpace(bearerToken))
            return SeoCallResult<SiteModelSnapshot>.Fail((int)HttpStatusCode.Unauthorized, "Signed-in user required to load site analysis.");

        var host = NormalizeHost(domain);

        // Real per-page h1–h6 tree, when available — see GetPageSectionTreesAsync. Non-fatal if
        // the trees call fails: related pages still stand on their URL; headings just degrade to
        // empty rather than failing the whole site model load.
        var treesResult = await GetPageSectionTreesAsync(profileId, bearerToken, ct);
        var trees = treesResult.Ok && treesResult.Value is not null
            ? treesResult.Value
            : new List<PageSectionTreeDto>();
        if (!treesResult.Ok)
        {
            _logger.LogWarning(
                "Site Analyzer page-section-trees unavailable for profile {ProfileId}: {Error}",
                profileId, treesResult.Error);
        }

        var urlsResult = await GetDiscoveredUrlsAsync(profileId, bearerToken, ct);
        if (!urlsResult.Ok)
            return SeoCallResult<SiteModelSnapshot>.Fail(urlsResult.StatusCode, urlsResult.Error!);
        var discoveredUrls = urlsResult.Value ?? [];

        var sitePages = BuildSitePagesFromDiscoveredUrls(discoveredUrls, trees);
        if (sitePages.Count == 0)
        {
            return SeoCallResult<SiteModelSnapshot>.Fail(
                (int)HttpStatusCode.UnprocessableEntity,
                "Site analysis has no existing pages with URLs. Content Creator cannot attach site section context.");
        }

        // Topical neighbors: the site's own real top-level headings (one per crawled page,
        // wherever present) — a raw fact about what else is on the site, not an opinionated
        // topic-selection result. No cap: every real heading is kept.
        var neighbors = trees
            .Select(t => FirstHeadingText(t.TreeJson))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var gapsResult = await GetContentGapsAsync(profileId, bearerToken, ct);
        if (!gapsResult.Ok)
            return SeoCallResult<SiteModelSnapshot>.Fail(gapsResult.StatusCode, gapsResult.Error!);
        var gaps = (gapsResult.Value ?? []).Select(g =>
        {
            var hierarchy = g.Hierarchy is { Count: > 0 }
                ? g.Hierarchy
                : null;
            var pathLabel = hierarchy is { Count: > 0 }
                ? string.Join(" › ", hierarchy)
                : g.SectionPath;
            var reason = string.IsNullOrWhiteSpace(pathLabel)
                ? $"No page found for heading \"{g.Topic}\""
                : $"No page found for heading \"{g.Topic}\" ({pathLabel})";
            return new LiveGap(
                Guid.NewGuid().ToString("N"),
                g.Topic,
                g.SectionPath,
                reason,
                hierarchy,
                g.SourcePageUrl);
        }).ToList();

        return SeoCallResult<SiteModelSnapshot>.Success(new SiteModelSnapshot(
            projectId,
            profileId,
            host,
            gaps,
            sitePages,
            neighbors));
    }

    /// <summary>The page's own first real heading (its natural title), or null if it has none.</summary>
    private static string? FirstHeadingText(string treeJson)
    {
        List<PageSectionDto>? roots;
        try
        {
            roots = JsonSerializer.Deserialize<List<PageSectionDto>>(treeJson, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
        if (roots is null) return null;
        return GccGenerateService.FlattenSections(roots)
            .Select(n => n.HeadingText)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
    }

    /// <summary>
    /// Real per-page heading+paragraph tree for this profile — the grounding data for the
    /// "must mention real sub-topics" Generate injection (see <c>GccGenerateService</c>).
    /// </summary>
    public async Task<SeoCallResult<List<PageSectionTreeDto>>> GetPageSectionTreesAsync(
        Guid profileId,
        string? bearerToken,
        CancellationToken ct)
    {
        if (!_enabled)
            return SeoCallResult<List<PageSectionTreeDto>>.Fail((int)HttpStatusCode.ServiceUnavailable, "Site Analyzer is not configured on GeekAPI (GEEK_SEO_API_URL).");
        if (string.IsNullOrWhiteSpace(bearerToken))
            return SeoCallResult<List<PageSectionTreeDto>>.Fail((int)HttpStatusCode.Unauthorized, "Signed-in user required to load page section trees.");

        var result = await SendAsync(HttpMethod.Get, $"api/seo/site-analyzer/{profileId}/page-section-trees", bearerToken, ct);
        if (!result.Ok) return SeoCallResult<List<PageSectionTreeDto>>.Fail(result.StatusCode, result.Error!);
        var trees = JsonSerializer.Deserialize<List<PageSectionTreeDto>>(result.Body!, JsonOpts) ?? [];
        return SeoCallResult<List<PageSectionTreeDto>>.Success(trees);
    }

    /// <summary>Raw discovered/crawled URL inventory — the "does a page already exist" side of
    /// Content Creator's gap check. A raw crawl fact, not an opinionated analysis result.</summary>
    private async Task<SeoCallResult<List<DiscoveredUrlDto>>> GetDiscoveredUrlsAsync(
        Guid profileId,
        string? bearerToken,
        CancellationToken ct)
    {
        if (!_enabled)
            return SeoCallResult<List<DiscoveredUrlDto>>.Fail((int)HttpStatusCode.ServiceUnavailable, "Site Analyzer is not configured on GeekAPI (GEEK_SEO_API_URL).");
        if (string.IsNullOrWhiteSpace(bearerToken))
            return SeoCallResult<List<DiscoveredUrlDto>>.Fail((int)HttpStatusCode.Unauthorized, "Signed-in user required to load discovered URLs.");

        var result = await SendAsync(HttpMethod.Get, $"api/seo/site-analyzer/{profileId}/discovered-urls", bearerToken, ct);
        if (!result.Ok) return SeoCallResult<List<DiscoveredUrlDto>>.Fail(result.StatusCode, result.Error!);
        var urls = JsonSerializer.Deserialize<List<DiscoveredUrlDto>>(result.Body!, JsonOpts) ?? [];
        return SeoCallResult<List<DiscoveredUrlDto>>.Success(urls);
    }

    /// <summary>Content gaps computed mechanically by Geek-SEO from raw crawl data: a real
    /// heading with no matching page. No pillar selection, no confidence score, no minimum
    /// length gate — see <c>SiteContentCoverageMatcher.CollectAllHeadingGaps</c>.</summary>
    private async Task<SeoCallResult<List<ContentGapRawDto>>> GetContentGapsAsync(
        Guid profileId,
        string? bearerToken,
        CancellationToken ct)
    {
        if (!_enabled)
            return SeoCallResult<List<ContentGapRawDto>>.Fail((int)HttpStatusCode.ServiceUnavailable, "Site Analyzer is not configured on GeekAPI (GEEK_SEO_API_URL).");
        if (string.IsNullOrWhiteSpace(bearerToken))
            return SeoCallResult<List<ContentGapRawDto>>.Fail((int)HttpStatusCode.Unauthorized, "Signed-in user required to load content gaps.");

        var result = await SendAsync(HttpMethod.Get, $"api/seo/site-analyzer/{profileId}/content-gaps", bearerToken, ct);
        if (!result.Ok) return SeoCallResult<List<ContentGapRawDto>>.Fail(result.StatusCode, result.Error!);
        var gaps = JsonSerializer.Deserialize<List<ContentGapRawDto>>(result.Body!, JsonOpts) ?? [];
        return SeoCallResult<List<ContentGapRawDto>>.Success(gaps);
    }

    /// <summary>
    /// Fetches the sitemap.xml built from the current step-1 URL inventory for this profile.
    /// Always current — Geek-SEO rebuilds it from the persisted inventory on every request, so
    /// there is nothing here to cache or go stale between Analyze runs.
    /// </summary>
    public async Task<SeoCallResult<string>> GetSitemapXmlAsync(
        Guid profileId,
        string? bearerToken,
        CancellationToken ct)
    {
        if (!_enabled)
            return SeoCallResult<string>.Fail((int)HttpStatusCode.ServiceUnavailable, "Site Analyzer is not configured on GeekAPI (GEEK_SEO_API_URL).");
        if (string.IsNullOrWhiteSpace(bearerToken))
            return SeoCallResult<string>.Fail((int)HttpStatusCode.Unauthorized, "Signed-in user required to download the sitemap.");

        var result = await SendAsync(HttpMethod.Get, $"api/seo/site-analyzer/{profileId}/sitemap.xml", bearerToken, ct);
        if (!result.Ok) return SeoCallResult<string>.Fail(result.StatusCode, result.Error!);
        return SeoCallResult<string>.Success(result.Body ?? string.Empty);
    }

    /// <summary>
    /// Existing site pages built directly from raw crawl facts — the discovered/crawled URL
    /// inventory plus each page's real heading tree. No pillar/subtopic model, no excerpt
    /// invention: a page's title is its own first real heading, or its URL if it has none.
    /// </summary>
    private static List<RelatedPageDto> BuildSitePagesFromDiscoveredUrls(
        List<DiscoveredUrlDto> discoveredUrls,
        List<PageSectionTreeDto> trees)
    {
        var headingsByUrl = BuildHeadingsByUrl(trees);
        var pages = new List<RelatedPageDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in discoveredUrls)
        {
            if (string.IsNullOrWhiteSpace(d.Url) || !seen.Add(NormalizeUrlKey(d.Url)))
                continue;

            var headings = HeadingsForUrl(d.Url, headingsByUrl);
            var title = headings.Length > 0 ? headings[0].Text : d.Url.Trim();
            pages.Add(new RelatedPageDto(d.Url.Trim(), title, headings, string.Empty));
        }

        return pages;
    }

    /// <summary>
    /// Flattens each page's real h1–h6 tree (<see cref="PageSectionDto"/>, as fetched by
    /// <see cref="GetPageSectionTreesAsync"/>) into leveled headings, keyed by normalized page URL.
    /// A page with no matching tree simply has no headings — never fabricated.
    /// </summary>
    private static Dictionary<string, HeadingDto[]> BuildHeadingsByUrl(List<PageSectionTreeDto> trees)
    {
        var byUrl = new Dictionary<string, HeadingDto[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var tree in trees)
        {
            if (string.IsNullOrWhiteSpace(tree.PageUrl)) continue;
            List<PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<PageSectionDto>>(tree.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }
            if (roots is null) continue;

            // No count cap here: this is the real crawled outline for an existing page, not
            // uploaded-research prompt injection (that's what MaxHeadingsPerPage bounds).
            // Truncating an individual absurdly-long heading string is still a reasonable guard;
            // truncating how many real headings a page is allowed to have is not.
            var headings = GccGenerateService.FlattenSections(roots)
                .Where(n => !string.IsNullOrWhiteSpace(n.HeadingText))
                .Select(n => new HeadingDto(
                    n.Level,
                    n.HeadingText.Length > GccResearchCaps.MaxHeadingChars
                        ? n.HeadingText[..GccResearchCaps.MaxHeadingChars]
                        : n.HeadingText))
                .ToArray();

            byUrl[NormalizeUrlKey(tree.PageUrl)] = headings;
        }
        return byUrl;
    }

    private static HeadingDto[] HeadingsForUrl(string? url, Dictionary<string, HeadingDto[]> headingsByUrl)
    {
        if (string.IsNullOrWhiteSpace(url)) return Array.Empty<HeadingDto>();
        return headingsByUrl.TryGetValue(NormalizeUrlKey(url), out var headings)
            ? headings
            : Array.Empty<HeadingDto>();
    }

    private static string NormalizeUrlKey(string url) => url.Trim().TrimEnd('/');

    private async Task<RawHttp> SendAsync(
        HttpMethod method,
        string relativeUrl,
        string bearerToken,
        CancellationToken ct,
        object? payload = null)
    {
        try
        {
            using var req = new HttpRequestMessage(method, relativeUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            if (payload is not null)
            {
                req.Content = JsonContent.Create(payload, options: JsonOpts);
            }
            var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode)
                return new RawHttp(true, (int)res.StatusCode, body, null);

            var err = TryReadError(body) ?? $"Site Analyzer returned {(int)res.StatusCode} for {relativeUrl}";
            _logger.LogWarning("Site Analyzer {Url} → {Status}: {Error}", relativeUrl, (int)res.StatusCode, err);
            return new RawHttp(false, (int)res.StatusCode, body, err);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Site Analyzer call failed: {Url}", relativeUrl);
            return new RawHttp(
                false,
                (int)HttpStatusCode.BadGateway,
                null,
                $"Site Analyzer is unreachable: {ex.Message}");
        }
    }

    private static string? TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
                return e.GetString();
            if (doc.RootElement.TryGetProperty("title", out var t))
                return t.GetString();
        }
        catch
        {
            /* raw body */
        }

        return body.Length > 240 ? body[..240] : body;
    }

    internal static string NormalizeHost(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();
        if (!s.Contains("://", StringComparison.Ordinal))
            s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            return raw.Trim().TrimEnd('/').ToLowerInvariant()
                .Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];
        return host;
    }

    private static bool HostsMatch(string a, string b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            || a.EndsWith("." + b, StringComparison.OrdinalIgnoreCase)
            || b.EndsWith("." + a, StringComparison.OrdinalIgnoreCase));

    private sealed record RawHttp(bool Ok, int StatusCode, string? Body, string? Error);

    public sealed record SeoProjectDto(Guid Id, string Name, string Url);

    /// <summary>Only <c>Id</c> is ever read (see <see cref="StartSiteAnalysisAsync"/>) — the
    /// pillar/subtopic fields this used to carry were deleted along with the site's-own-pillar
    /// pipeline; gaps and existing pages are now read as raw crawl facts instead (see
    /// <see cref="DiscoveredUrlDto"/>, <see cref="ContentGapRawDto"/>).</summary>
    private sealed record SeoProfileDto(Guid Id);

    /// <summary>Raw discovered/crawled URL — one row of the site's real URL inventory, no
    /// opinion attached.</summary>
    private sealed record DiscoveredUrlDto(string Url, string SourceType);

    /// <summary>A real heading with no matching page — Content Creator's entire gap rule,
    /// computed mechanically in Geek-SEO from raw crawl data (see
    /// <c>SiteContentCoverageMatcher.CollectAllHeadingGaps</c>). No pillar, no confidence score.</summary>
    private sealed record ContentGapRawDto(
        string Topic,
        string? SectionPath,
        string? SourcePageUrl = null,
        IReadOnlyList<string>? Hierarchy = null);

    public sealed record SiteAnalysisStatus(
        string? Status,
        string? Step = null,
        int StepNumber = 0,
        int TotalSteps = 0,
        string? ErrorMessage = null)
    {
        public bool IsComplete => string.Equals(Status, "complete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "ready", StringComparison.OrdinalIgnoreCase);

        public bool IsFailed => string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Status, "error", StringComparison.OrdinalIgnoreCase);
    }

    public sealed record LiveGap(
        string Id,
        string Topic,
        string? SectionPath,
        string Reason,
        IReadOnlyList<string>? Hierarchy = null,
        string? SourcePageUrl = null);

    public sealed record PageSectionLinkDto(
        string Text,
        string Href);

    /// <summary>One crawled page's real heading tree (mirrors Geek-SEO's PageSection JSON shape).</summary>
    public sealed record PageSectionDto(
        int Level,
        string HeadingText,
        List<string>? Paragraphs,
        List<PageSectionLinkDto>? Links,
        List<PageSectionDto>? Children);

    public sealed record PageSectionTreeDto(
        Guid Id,
        Guid SiteAnalysisProfileId,
        string PageUrl,
        string TreeJson,
        DateTimeOffset CreatedAtUtc);

    public sealed record SiteModelSnapshot(
        Guid SeoProjectId,
        Guid SeoProfileId,
        string Domain,
        IReadOnlyList<LiveGap> Gaps,
        IReadOnlyList<RelatedPageDto> SitePages,
        IReadOnlyList<string> TopicalNeighbors);
}

public sealed record SeoCallResult<T>(bool Ok, T? Value, int StatusCode, string? Error)
{
    public static SeoCallResult<T> Success(T value) => new(true, value, 200, null);
    public static SeoCallResult<T> Fail(int status, string error) => new(false, default, status, error);
}

public class GccJobStore
{
    private readonly ConcurrentDictionary<Guid, GccJob> _jobs = new();

    public GccJob Create(string kind, Guid createId)
    {
        var job = new GccJob(Guid.NewGuid(), kind, createId, "running", null, null, DateTime.UtcNow, null);
        _jobs[job.Id] = job;
        return job;
    }

    public void Complete(Guid id, object? result) =>
        Update(id, j => j with { Status = "ready", ResultJson = JsonSerializer.Serialize(result), CompletedAtUtc = DateTime.UtcNow });

    public void Fail(Guid id, string error) =>
        Update(id, j => j with { Status = "failed", Error = error, CompletedAtUtc = DateTime.UtcNow });

    public GccJob? Get(Guid id) => _jobs.TryGetValue(id, out var j) ? j : null;

    private void Update(Guid id, Func<GccJob, GccJob> mutator)
    {
        _jobs.AddOrUpdate(id, _ => throw new KeyNotFoundException(), (_, cur) => mutator(cur));
    }
}

public sealed record GccJob(
    Guid Id,
    string Kind,
    Guid CreateId,
    string Status,
    string? ResultJson,
    string? Error,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);
