using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

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
        var profileRes = await SendAsync(HttpMethod.Get, $"api/seo/site-analyzer/{profileId}", bearerToken, ct);
        if (!profileRes.Ok) return SeoCallResult<SiteModelSnapshot>.Fail(profileRes.StatusCode, profileRes.Error!);
        var profile = JsonSerializer.Deserialize<SeoProfileDto>(profileRes.Body!, JsonOpts);
        if (profile is null)
            return SeoCallResult<SiteModelSnapshot>.Fail((int)HttpStatusCode.BadGateway, "Site Analyzer returned an invalid profile.");

        var gapsRes = await SendAsync(
            HttpMethod.Get,
            $"api/seo/site-analyzer/{profile.Id}/gaps?quickWinsOnly=false",
            bearerToken,
            ct);
        if (!gapsRes.Ok)
            return SeoCallResult<SiteModelSnapshot>.Fail(gapsRes.StatusCode, gapsRes.Error!);

        var gapDtos = JsonSerializer.Deserialize<List<LiveGapDto>>(gapsRes.Body!, JsonOpts) ?? [];
        var gaps = gapDtos.Select(g => new LiveGap(
            g.SubtopicId == Guid.Empty ? Guid.NewGuid().ToString("N") : g.SubtopicId.ToString("D"),
            string.IsNullOrWhiteSpace(g.TargetKeyword) ? g.SubtopicTitle : g.TargetKeyword,
            g.PillarTopic,
            g.IsQuickWin
                ? "Quick-win topical gap"
                : $"Gap under {g.PillarTopic} ({g.RecommendedFormat})",
            string.Equals(g.RecommendedFormat, "pillar", StringComparison.OrdinalIgnoreCase)
                || g.RecommendedFormat.Contains("pillar", StringComparison.OrdinalIgnoreCase))).ToList();

        var sitePages = BuildSitePagesFromProfile(profile);
        var neighbors = profile.Pillars
            .Select(p => p.PillarTopic)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();

        if (sitePages.Count == 0)
        {
            return SeoCallResult<SiteModelSnapshot>.Fail(
                (int)HttpStatusCode.UnprocessableEntity,
                "Site analysis has no existing pages with URLs. Content Creator cannot attach site section context.");
        }

        return SeoCallResult<SiteModelSnapshot>.Success(new SiteModelSnapshot(
            projectId,
            profile.Id,
            host,
            gaps,
            sitePages,
            neighbors));
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

    private static List<RelatedPageDto> BuildSitePagesFromProfile(SeoProfileDto profile)
    {
        var pages = new List<RelatedPageDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pillar in profile.Pillars)
        {
            if (!string.IsNullOrWhiteSpace(pillar.PageUrl) && seen.Add(pillar.PageUrl.Trim()))
            {
                var headings = pillar.Subtopics
                    .Where(s => !string.IsNullOrWhiteSpace(s.ExistingUrl)
                                || string.Equals(s.CoverageStatus, "covered", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(s.CoverageStatus, "partial", StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.SubtopicTitle)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Take(8)
                    .ToArray();
                var excerpt = !string.IsNullOrWhiteSpace(pillar.ContentAngle)
                    ? pillar.ContentAngle!.Trim()
                    : $"Existing pillar page for “{pillar.PillarTopic}” on the analyzed site.";
                pages.Add(new RelatedPageDto(
                    pillar.PageUrl.Trim(),
                    pillar.PillarTopic,
                    headings,
                    excerpt));
            }

            foreach (var sub in pillar.Subtopics)
            {
                if (string.IsNullOrWhiteSpace(sub.ExistingUrl) || !seen.Add(sub.ExistingUrl.Trim()))
                    continue;
                var excerpt = !string.IsNullOrWhiteSpace(sub.TargetKeyword)
                    ? $"Existing coverage for “{sub.TargetKeyword}” under {pillar.PillarTopic}."
                    : $"Existing page under {pillar.PillarTopic}.";
                pages.Add(new RelatedPageDto(
                    sub.ExistingUrl.Trim(),
                    sub.SubtopicTitle,
                    Array.Empty<string>(),
                    excerpt));
            }
        }

        return pages;
    }

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

    private sealed record SeoProfileDto(
        Guid Id,
        Guid ProjectId,
        string Domain,
        string? PrimaryFocus,
        string? FocusDescription,
        List<SeoPillarDto> Pillars);

    private sealed record SeoPillarDto(
        Guid Id,
        string PillarTopic,
        string? PageUrl,
        string? ContentAngle,
        List<SeoSubtopicDto> Subtopics);

    private sealed record SeoSubtopicDto(
        Guid Id,
        string SubtopicTitle,
        string TargetKeyword,
        string? CoverageStatus,
        string? ExistingUrl,
        string RecommendedFormat,
        bool IsQuickWin);

    private sealed record LiveGapDto(
        Guid SubtopicId,
        string PillarTopic,
        string SubtopicTitle,
        string TargetKeyword,
        bool IsQuickWin,
        string RecommendedFormat);

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
        bool SuggestPillar);

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
