using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.BrandKit;

/// <summary>
/// Derives a provisional brand/voice kit from the Geek-SEO crawl attached to a create
/// (<c>site_analysis_profiles</c>). Read-only via existing GeekRepository schema-signals and
/// <see cref="HttpGeekSeoSiteAnalyzerClient"/> page contexts/trees — never SA2, never edits Geek-SEO.
/// Fails hard if company/website identity cannot be grounded.
/// </summary>
public sealed class GccV2BrandKitBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Regex AudienceForPattern = new(
        @"\b(?:for|helping|serving)\s+([^.!?\n]{8,80})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HttpGeekSeoSiteAnalyzerClient _seo;
    private readonly ILogger<GccV2BrandKitBuilder> _logger;

    public GccV2BrandKitBuilder(
        IHttpClientFactory httpClientFactory,
        HttpGeekSeoSiteAnalyzerClient seo,
        ILogger<GccV2BrandKitBuilder> logger)
    {
        _httpClientFactory = httpClientFactory;
        _seo = seo;
        _logger = logger;
    }

    public async Task<GccV2BrandKitContent> BuildAsync(
        Guid siteAnalysisProfileId,
        string bearerToken,
        Guid ownerUserId,
        string? siteUrl,
        SiteSectionContextDto? section,
        CancellationToken ct)
    {
        try
        {
            if (siteAnalysisProfileId == Guid.Empty)
                throw new InvalidOperationException("siteAnalysisProfileId is required to build a brand kit.");
            if (string.IsNullOrWhiteSpace(bearerToken))
                throw new InvalidOperationException("Signed-in user required to load crawl facts for BrandKit.");

            var signals = await LoadSchemaSignalsAsync(siteAnalysisProfileId, ownerUserId, ct);
            var pagesResult = await _seo.GetPageContextsAsync(siteAnalysisProfileId, bearerToken, ct);
            if (!pagesResult.Ok)
            {
                throw new InvalidOperationException(
                    pagesResult.Error ?? $"Could not load page contexts for profile {siteAnalysisProfileId}.");
            }

            var pages = pagesResult.Value ?? [];
            var treesResult = await _seo.GetPageSectionTreesAsync(siteAnalysisProfileId, bearerToken, ct);
            var trees = treesResult.Ok && treesResult.Value is not null
                ? treesResult.Value
                : new List<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto>();
            if (!treesResult.Ok)
            {
                _logger.LogWarning(
                    "Page-section trees unavailable for BrandKit profile {ProfileId}: {Error}",
                    siteAnalysisProfileId,
                    treesResult.Error);
            }

            var website = FirstNonEmpty(
                NormalizeWebsite(siteUrl),
                HomepageUrl(pages),
                section?.RelatedPages?.Select(p => p.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)));

            var homepage = PickHomepage(pages, website);
            var about = PickAbout(pages);

            var companyName = FirstNonEmpty(
                SignalValue(signals, "organization", "brandName"),
                CleanTitle(homepage?.Title),
                FirstHeading(homepage),
                HostFromUrl(website));

            var companyDescription = FirstNonEmpty(
                SignalValue(signals, "organization", "description"),
                homepage?.Description,
                about?.Description,
                FirstParagraph(homepage?.Markdown),
                FirstParagraph(about?.Markdown));

            var tagline = FirstNonEmpty(
                FirstHeading(homepage),
                CleanTitle(homepage?.Title));

            var positioning = FirstNonEmpty(
                PositioningFromTrees(trees, website),
                FirstParagraph(about?.Markdown),
                companyDescription);

            var knowsAbout = DistinctNonEmpty(
                SignalValues(signals, "thing", "knowsAbout")
                    .Concat(SignalValues(signals, "offer_catalog", "serviceType")));

            var features = DistinctNonEmpty(
                SignalValues(signals, "service", "name")
                    .Concat(knowsAbout.Take(12))
                    .Concat(ServiceHeadings(trees)));

            var areaServed = DistinctNonEmpty(SignalValues(signals, "organization", "areaServed"));
            var sameAs = DistinctNonEmpty(SignalValues(signals, "organization", "sameAs"));

            var voiceSamples = BuildVoiceSamples(pages, website, section);
            var ctaPhrases = ExtractCtaPhrases(trees);
            var audiences = ExtractAudiences(homepage?.Markdown, about?.Markdown);

            var notes = new List<string>();
            if (signals.Count == 0)
                notes.Add("No schema-signals on this crawl — kit fields filled from page contexts/trees where possible.");
            if (voiceSamples.Count == 0)
                notes.Add("No page body samples available — review/edit voice on Accept.");
            if (audiences.Count == 0)
                notes.Add("Audience not clear from homepage/about — add on Accept if needed.");
            if (section?.RelatedPages is { Count: > 0 } related)
                notes.Add($"{related.Count} related page(s) from create site section for internal links.");

            var kit = new GccV2BrandKitContent
            {
                CompanyName = companyName,
                Website = website,
                CompanyDescription = companyDescription,
                Tagline = tagline,
                PositioningOneLiner = positioning,
                Audiences = audiences,
                Features = features,
                KnowsAbout = knowsAbout,
                AreaServed = areaServed,
                SameAs = sameAs,
                VoiceSamples = voiceSamples,
                VoiceGuidance =
                [
                    "Provisional kit from Geek-SEO crawl — review samples and Accept before write.",
                ],
                VoiceStatus = "provisional",
                CtaPhrases = ctaPhrases,
                Notes = notes,
            };

            if (string.IsNullOrWhiteSpace(kit.CompanyName) && string.IsNullOrWhiteSpace(kit.Website))
            {
                throw new InvalidOperationException(
                    "Brand kit from crawl is empty (no company name or website) — refuse to write without site identity.");
            }

            return kit;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brand kit build failed for profile {ProfileId}.", siteAnalysisProfileId);
            throw new InvalidOperationException(
                $"Brand kit build failed for profile {siteAnalysisProfileId}: {ex.Message}", ex);
        }
    }

    private async Task<IReadOnlyList<SchemaSignalDto>> LoadSchemaSignalsAsync(
        Guid profileId,
        Guid ownerUserId,
        CancellationToken ct)
    {
        try
        {
            var http = _httpClientFactory.CreateClient("GeekRepository");
            var path =
                $"repo/seo/site-analysis-profiles/{profileId:D}/schema-signals?userId={ownerUserId:D}";
            using var res = await http.GetAsync(path, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "schema-signals HTTP {Status} for profile {ProfileId}; continuing with page contexts.",
                    (int)res.StatusCode,
                    profileId);
                return [];
            }

            var rows = await res.Content.ReadFromJsonAsync<List<SchemaSignalDto>>(JsonOpts, ct);
            return rows ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "schema-signals load failed for profile {ProfileId}; continuing.", profileId);
            return [];
        }
    }

    private sealed class SchemaSignalDto
    {
        public string SchemaType { get; init; } = "";
        public string PropertyName { get; init; } = "";
        public string PropertyValue { get; init; } = "";
    }

    private static IReadOnlyList<string> BuildVoiceSamples(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageContextDto> pages,
        string? website,
        SiteSectionContextDto? section)
    {
        var preferred = PreferHighSignalPages(pages, website);
        var samples = new List<string>();
        var totalChars = 0;

        foreach (var page in preferred)
        {
            var excerpt = FirstParagraphs(page.Markdown, maxChars: 900);
            if (string.IsNullOrWhiteSpace(excerpt) && !string.IsNullOrWhiteSpace(page.Description))
                excerpt = page.Description.Trim();
            if (string.IsNullOrWhiteSpace(excerpt))
                continue;

            var label = string.IsNullOrWhiteSpace(page.PageUrl) ? excerpt : $"{page.PageUrl}\n{excerpt}";
            samples.Add(label);
            totalChars += excerpt.Length;
            if (totalChars >= 1000 && samples.Count >= 2)
                break;
        }

        if (samples.Count == 0 && section?.RelatedPages is { Count: > 0 } related)
        {
            foreach (var p in related)
            {
                if (string.IsNullOrWhiteSpace(p.Excerpt) && string.IsNullOrWhiteSpace(p.Title))
                    continue;
                var body = string.IsNullOrWhiteSpace(p.Excerpt) ? p.Title : $"{p.Title}: {p.Excerpt}";
                samples.Add(string.IsNullOrWhiteSpace(p.Url) ? body : $"{p.Url}\n{body}");
            }
        }

        return samples;
    }

    private static IEnumerable<HttpGeekSeoSiteAnalyzerClient.PageContextDto> PreferHighSignalPages(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageContextDto> pages,
        string? website)
    {
        static int Score(HttpGeekSeoSiteAnalyzerClient.PageContextDto p, string? site)
        {
            var url = (p.PageUrl ?? "").ToLowerInvariant();
            var score = 0;
            if (IsHomepageUrl(url, site)) score += 100;
            if (url.Contains("/about")) score += 80;
            if (url.Contains("/company") || url.Contains("/who-we")) score += 60;
            if (url.Contains("/service") || url.Contains("/product") || url.Contains("/solution")) score += 40;
            if (url.Contains("/blog") || url.Contains("/pillar")) score += 20;
            if (!string.IsNullOrWhiteSpace(p.Markdown)) score += Math.Min(30, p.Markdown!.Length / 200);
            return score;
        }

        return pages
            .Where(p => !string.IsNullOrWhiteSpace(p.PageUrl))
            .OrderByDescending(p => Score(p, website));
    }

    private static IReadOnlyList<string> ExtractCtaPhrases(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> trees)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tree in trees)
        {
            List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(
                    tree.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }

            if (roots is null) continue;
            foreach (var node in GccGenerateService.FlattenSections(roots))
            {
                if (node.Links is null) continue;
                foreach (var link in node.Links)
                {
                    var text = (link.Text ?? "").Trim();
                    if (text.Length is < 2 or > 60) continue;
                    if (text.Contains("http", StringComparison.OrdinalIgnoreCase)) continue;
                    counts[text] = counts.TryGetValue(text, out var n) ? n + 1 : 1;
                }
            }
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .Take(12)
            .ToList();
    }

    private static IReadOnlyList<string> ExtractAudiences(string? homepageMd, string? aboutMd)
    {
        var found = new List<string>();
        foreach (var text in new[] { homepageMd, aboutMd })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            foreach (Match m in AudienceForPattern.Matches(text))
            {
                var phrase = m.Groups[1].Value.Trim().TrimEnd(',', ';');
                if (phrase.Length < 8) continue;
                if (found.Any(f => f.Equals(phrase, StringComparison.OrdinalIgnoreCase))) continue;
                found.Add(phrase);
                if (found.Count >= 5) return found;
            }
        }

        return found;
    }

    private static string? PositioningFromTrees(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> trees,
        string? website)
    {
        foreach (var tree in trees.OrderByDescending(t => IsHomepageUrl(t.PageUrl, website) ? 1 : 0)
                     .ThenByDescending(t => (t.PageUrl ?? "").Contains("/about", StringComparison.OrdinalIgnoreCase)))
        {
            List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(
                    tree.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }

            if (roots is null) continue;
            foreach (var node in GccGenerateService.FlattenSections(roots))
            {
                var heading = node.HeadingText ?? "";
                if (!LooksLikeWhoWeAre(heading)) continue;
                var para = node.Paragraphs?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                if (!string.IsNullOrWhiteSpace(para))
                    return Truncate(para.Trim(), 280);
            }
        }

        return null;
    }

    private static IEnumerable<string> ServiceHeadings(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> trees)
    {
        foreach (var tree in trees)
        {
            var url = (tree.PageUrl ?? "").ToLowerInvariant();
            if (!(url.Contains("/service") || url.Contains("/product") || url.Contains("/solution")))
                continue;

            List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(
                    tree.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }

            if (roots is null) continue;
            foreach (var node in GccGenerateService.FlattenSections(roots))
            {
                if (node.Level is >= 2 and <= 3 && !string.IsNullOrWhiteSpace(node.HeadingText))
                    yield return node.HeadingText.Trim();
            }
        }
    }

    private static bool LooksLikeWhoWeAre(string heading)
    {
        var h = heading.ToLowerInvariant();
        return h.Contains("who we")
               || h.Contains("what we")
               || h.Contains("about us")
               || h.Contains("our mission")
               || h.Contains("we help")
               || h == "about";
    }

    private static string? SignalValue(
        IReadOnlyList<SchemaSignalDto> signals,
        string schemaType,
        string propertyName) =>
        SignalValues(signals, schemaType, propertyName).FirstOrDefault();

    private static IEnumerable<string> SignalValues(
        IReadOnlyList<SchemaSignalDto> signals,
        string schemaType,
        string propertyName) =>
        signals
            .Where(s =>
                s.SchemaType.Equals(schemaType, StringComparison.OrdinalIgnoreCase)
                && s.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(s.PropertyValue))
            .Select(s => s.PropertyValue.Trim());

    private static HttpGeekSeoSiteAnalyzerClient.PageContextDto? PickHomepage(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageContextDto> pages,
        string? website) =>
        pages.FirstOrDefault(p => IsHomepageUrl(p.PageUrl, website))
        ?? pages.OrderBy(p => (p.PageUrl ?? "").Length).FirstOrDefault();

    private static HttpGeekSeoSiteAnalyzerClient.PageContextDto? PickAbout(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageContextDto> pages) =>
        pages.FirstOrDefault(p => (p.PageUrl ?? "").Contains("/about", StringComparison.OrdinalIgnoreCase));

    private static string? HomepageUrl(IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageContextDto> pages) =>
        pages.Select(p => p.PageUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

    private static bool IsHomepageUrl(string? url, string? website)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path is "" or "/" or "/index" or "/index.html" or "/home")
            return true;
        if (!string.IsNullOrWhiteSpace(website)
            && Uri.TryCreate(NormalizeWebsite(website), UriKind.Absolute, out var site)
            && string.Equals(uri.Host, site.Host, StringComparison.OrdinalIgnoreCase)
            && path.Length <= 1)
            return true;
        return false;
    }

    private static string? FirstHeading(HttpGeekSeoSiteAnalyzerClient.PageContextDto? page) =>
        page?.Headings?.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h))?.Trim();

    private static string? CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var t = title.Trim();
        var parts = t.Split(['|', '—', '–', '-'], 2, StringSplitOptions.TrimEntries);
        return parts[0].Length >= 2 ? parts[0] : t;
    }

    private static string? FirstParagraph(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        return FirstParagraphs(markdown, maxChars: 320);
    }

    private static string? FirstParagraphs(string? markdown, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var lines = markdown
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Where(l => !l.StartsWith('#'))
            .Where(l => !l.StartsWith("```", StringComparison.Ordinal))
            .Where(l => !l.StartsWith('!') && !l.StartsWith('['))
            .ToList();
        if (lines.Count == 0) return null;

        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(line);
            if (sb.Length >= maxChars) break;
        }

        return Truncate(sb.ToString(), maxChars);
    }

    private static string? Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        return value[..max].TrimEnd() + "…";
    }

    private static string? NormalizeWebsite(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim().TrimEnd('/');
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "https://" + trimmed;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}{(uri.AbsolutePath is "/" or "" ? "" : uri.AbsolutePath.TrimEnd('/'))}"
            : trimmed;
    }

    private static string? HostFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static IReadOnlyList<string> DistinctNonEmpty(IEnumerable<string> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
}

/// <summary>Shape persisted as <c>GccV2BrandKit.KitJson</c> and summarized in the
/// <c>BrandKitReady</c> job event.</summary>
public sealed record GccV2BrandKitContent
{
    public string? CompanyName { get; init; }
    public string? Website { get; init; }
    public string? CompanyDescription { get; init; }
    public string? Tagline { get; init; }
    public string? PositioningOneLiner { get; init; }
    public IReadOnlyList<string> Audiences { get; init; } = [];
    public IReadOnlyList<string> Features { get; init; } = [];
    public IReadOnlyList<string> KnowsAbout { get; init; } = [];
    public IReadOnlyList<string> AreaServed { get; init; } = [];
    public IReadOnlyList<string> SameAs { get; init; } = [];
    public IReadOnlyList<string> VoiceSamples { get; init; } = [];
    public IReadOnlyList<string> VoiceGuidance { get; init; } = [];
    public string VoiceStatus { get; init; } = "provisional";
    public IReadOnlyList<string> CtaPhrases { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}
