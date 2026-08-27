using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator.Polite;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Polite partner-destination crawl + extract + persist audit/cache rows.
/// Soft per-URL failures — never throws for a single bad href.
/// </summary>
public sealed class GccPartnerUrlResearchService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IGccPoliteCrawler _crawler;
    private readonly HttpGccV2Repository _repo;
    private readonly ILogger<GccPartnerUrlResearchService> _logger;

    public GccPartnerUrlResearchService(
        IGccPoliteCrawler crawler,
        HttpGccV2Repository repo,
        ILogger<GccPartnerUrlResearchService> logger)
    {
        _crawler = crawler;
        _repo = repo;
        _logger = logger;
    }

    /// <summary>
    /// Hrefs to fetch for weave excerpts. Prefer operator destination URLs attached to crawl
    /// tools; fall back to absolute crawl hrefs when no operator URL is attached.
    /// </summary>
    public static IReadOnlyList<string> CollectPartnerHrefs(string? rawBriefJson) =>
        CollectPartnerToolRows(rawBriefJson)
            .Where(t => !string.IsNullOrWhiteSpace(t.Url))
            .OrderBy(t => string.Equals(t.Source, "operator", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(t => t.Url!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(GccPartnerResearchCaps.MaxUrls)
            .ToList();

    /// <summary>
    /// Preflight tool rows = crawl <c>hierarchyPlan.recommendedTools</c> only.
    /// Operator paste attaches destination URLs for excerpts; bare URLs alone never invent tools.
    /// </summary>
    public static IReadOnlyList<PartnerToolRow> CollectPartnerToolRows(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return [];

        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            var root = doc.RootElement;

            var rows = new List<PartnerToolRow>();
            if (TryGetPropertyIgnoreCase(root, "hierarchyPlan", out var plan)
                && plan.ValueKind == JsonValueKind.Object
                && TryGetPropertyIgnoreCase(plan, "recommendedTools", out var tools)
                && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tools.EnumerateArray())
                {
                    if (rows.Count >= GccPartnerResearchCaps.MaxUrls) break;
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    var name = TryGetPropertyIgnoreCase(t, "name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString()?.Trim()
                        : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (rows.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

                    var href = TryGetPropertyIgnoreCase(t, "href", out var h) && h.ValueKind == JsonValueKind.String
                        ? h.GetString()
                        : TryGetPropertyIgnoreCase(t, "url", out var u) && u.ValueKind == JsonValueKind.String
                            ? u.GetString()
                            : null;
                    string? url = null;
                    if (!string.IsNullOrWhiteSpace(href) && TryNormalizeHttpUrl(href, out var normalized))
                        url = normalized;
                    rows.Add(new PartnerToolRow(name!, url, "crawl"));
                }
            }

            if (rows.Count == 0) return [];

            if (TryGetPropertyIgnoreCase(root, "operatorTools", out var ops)
                && ops.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in ops.EnumerateArray())
                {
                    string? opName = null;
                    string? opUrl = null;
                    if (t.ValueKind == JsonValueKind.String)
                    {
                        opUrl = t.GetString();
                    }
                    else if (t.ValueKind == JsonValueKind.Object)
                    {
                        opName = TryGetPropertyIgnoreCase(t, "name", out var n) && n.ValueKind == JsonValueKind.String
                            ? n.GetString()?.Trim()
                            : null;
                        opUrl = TryGetPropertyIgnoreCase(t, "url", out var u) && u.ValueKind == JsonValueKind.String
                            ? u.GetString()
                            : TryGetPropertyIgnoreCase(t, "href", out var h) && h.ValueKind == JsonValueKind.String
                                ? h.GetString()
                                : null;
                    }
                    else continue;

                    if (string.IsNullOrWhiteSpace(opUrl) || !TryNormalizeHttpUrl(opUrl, out var dest))
                        continue;

                    var idx = FindCrawlToolIndex(rows, opName, dest);
                    if (idx < 0) continue;
                    rows[idx] = rows[idx] with { Url = dest, Source = "operator" };
                }
            }

            return rows;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public sealed record PartnerToolRow(string Name, string? Url, string Source);

    private static int FindCrawlToolIndex(IReadOnlyList<PartnerToolRow> rows, string? opName, string destUrl)
    {
        if (!string.IsNullOrWhiteSpace(opName))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Name.Equals(opName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        var hostGuess = HostLabelFromUrl(destUrl);
        if (string.IsNullOrWhiteSpace(hostGuess)) return -1;

        for (var i = 0; i < rows.Count; i++)
        {
            var compactTool = CompactAlnum(rows[i].Name);
            var compactHost = CompactAlnum(hostGuess);
            if (compactTool.Length == 0 || compactHost.Length == 0) continue;
            if (compactTool == compactHost
                || compactTool.Contains(compactHost, StringComparison.Ordinal)
                || compactHost.Contains(compactTool, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static string HostLabelFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.Trim().TrimStart('.');
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                host = host[4..];
            return host.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }
        catch (UriFormatException)
        {
            return "";
        }
    }

    private static string CompactAlnum(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant);
        return string.Concat(chars);
    }

    public async Task<IReadOnlyList<GccQuoteablePage>> FetchAsync(
        Guid createId,
        IReadOnlyList<string> urls,
        CancellationToken ct)
    {
        if (urls.Count == 0) return [];

        var gate = new SemaphoreSlim(GccPartnerResearchCaps.MaxConcurrentFetches);
        var tasks = urls.Select(async url =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await FetchOneAsync(createId, url, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        var pages = await Task.WhenAll(tasks).ConfigureAwait(false);
        return pages.Where(p => p is not null).Cast<GccQuoteablePage>().ToList();
    }

    /// <summary>Writes <c>partnerResearch</c> onto the brief JSON (replaces any prior value).</summary>
    public static string? MergePartnerResearchIntoBriefJson(
        string? rawBriefJson,
        IReadOnlyList<GccQuoteablePage> pages)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBriefJson) ? "{}" : rawBriefJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return rawBriefJson;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "partnerResearch", StringComparison.OrdinalIgnoreCase))
                        continue;
                    prop.WriteTo(writer);
                }

                writer.WritePropertyName("partnerResearch");
                JsonSerializer.Serialize(writer, pages, JsonOpts);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return rawBriefJson;
        }
    }

    private async Task<GccQuoteablePage?> FetchOneAsync(Guid createId, string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _logger.LogWarning("[partner crawl] Invalid URL skipped: {Url}", url);
            await TryPersistAsync(createId, url, "", false, "InvalidUrl", null, null, null, ct).ConfigureAwait(false);
            return null;
        }

        var host = uri.Host;

        try
        {
            var cached = await TryGetFreshCacheAsync(url, ct).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogInformation("[partner crawl] Cache hit for {Url}", url);
                return cached;
            }

            var fetch = await _crawler.GetHtmlAsync(uri, ct).ConfigureAwait(false);
            if (!fetch.HasHtml)
            {
                await TryPersistAsync(createId, url, host, false, fetch.Status, null, null, null, ct)
                    .ConfigureAwait(false);
                return null;
            }

            var page = GccArticleHtmlExtractor.ExtractPartnerPage(url, fetch.Html!);
            if (GccArticleHtmlExtractor.IsEmpty(page))
            {
                await TryPersistAsync(
                        createId, url, host, false, GccPoliteFetchResult.Statuses.ExtractFailed, null, null, null, ct)
                    .ConfigureAwait(false);
                return null;
            }

            var pageJson = JsonSerializer.Serialize(page, JsonOpts);
            var flat = FlattenPage(page);
            await TryPersistAsync(
                    createId, url, host, true, GccPoliteFetchResult.Statuses.Success,
                    page.Title, pageJson, flat, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "[partner crawl] ok {Url}: {HeadingCount} headings, {ParagraphCount} paragraphs",
                url, page.Headings.Count, page.Paragraphs.Count);
            return page;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[partner crawl] failed for {Url}", url);
            await TryPersistAsync(
                    createId, url, host, false, GccPoliteFetchResult.Statuses.RequestFailed, null, null, null, ct)
                .ConfigureAwait(false);
            return null;
        }
    }

    private async Task<GccQuoteablePage?> TryGetFreshCacheAsync(string url, CancellationToken ct)
    {
        try
        {
            var row = await _repo.GetFreshPartnerResearchAsync(
                url, GccPartnerResearchCaps.CacheFreshnessHours, ct).ConfigureAwait(false);
            if (row is null || string.IsNullOrWhiteSpace(row.PageJson)) return null;
            return JsonSerializer.Deserialize<GccQuoteablePage>(row.PageJson, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[partner crawl] Fresh-cache lookup failed for {Url}", url);
            return null;
        }
    }

    private async Task TryPersistAsync(
        Guid createId,
        string url,
        string host,
        bool success,
        string status,
        string? title,
        string? pageJson,
        string? flat,
        CancellationToken ct)
    {
        if (createId == Guid.Empty) return;
        try
        {
            await _repo.CreatePartnerResearchRecordAsync(
                new CreateGccV2PartnerResearchRecordCommand(
                    createId,
                    url,
                    success,
                    status,
                    host,
                    JobId: null,
                    title,
                    pageJson,
                    flat),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[partner crawl] Persist failed for {Url} ({Status})", url, status);
        }
    }

    private static string FlattenPage(GccQuoteablePage page)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(page.Title))
            sb.AppendLine(page.Title);
        foreach (var h in page.Headings)
            sb.AppendLine($"H{h.Level}: {h.Text}");
        foreach (var p in page.Paragraphs)
            sb.AppendLine(p);
        return sb.ToString();
    }

    private static bool TryNormalizeHttpUrl(string? raw, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.StartsWith("//"))
                trimmed = "https:" + trimmed;
            else
                return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrWhiteSpace(uri.Host)) return false;
        url = uri.GetLeftPart(UriPartial.Query);
        if (url.EndsWith('/') && uri.AbsolutePath == "/")
            url = url.TrimEnd('/');
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
