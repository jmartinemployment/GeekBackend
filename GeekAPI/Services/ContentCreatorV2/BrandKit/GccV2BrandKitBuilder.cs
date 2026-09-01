using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;

namespace GeekAPI.Services.ContentCreatorV2.BrandKit;

/// <summary>
/// Derives a provisional brand/voice kit from owned project-site crawl pages.
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

    private readonly HttpGccV2Repository _repo;
    private readonly ILogger<GccV2BrandKitBuilder> _logger;

    public GccV2BrandKitBuilder(HttpGccV2Repository repo, ILogger<GccV2BrandKitBuilder> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<GccV2BrandKitContent> BuildAsync(
        Guid projectSiteCrawlRunId,
        string? siteUrl,
        SiteSectionContextDto? section,
        CancellationToken ct)
    {
        if (projectSiteCrawlRunId == Guid.Empty)
            throw new InvalidOperationException("projectSiteCrawlRunId is required to build a brand kit.");

        var pages = await LoadAllPagesAsync(projectSiteCrawlRunId, ct);
        if (pages.Count == 0)
            throw new InvalidOperationException($"No pages on project-site crawl run {projectSiteCrawlRunId}.");

        var website = FirstNonEmpty(
            NormalizeWebsite(siteUrl),
            HomepageUrl(pages),
            section?.RelatedPages?.Select(p => p.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)));

        var homepage = PickHomepage(pages, website);
        var about = PickAbout(pages);

        var companyName = FirstNonEmpty(
            CleanTitle(HomepageTitle(homepage)),
            FirstHeading(homepage),
            HostFromUrl(website));

        var companyDescription = FirstNonEmpty(
            FirstParagraph(homepage),
            FirstParagraph(about));

        var tagline = FirstNonEmpty(FirstHeading(homepage), CleanTitle(HomepageTitle(homepage)));

        var positioning = FirstNonEmpty(
            FirstParagraph(about),
            FirstParagraph(homepage),
            companyDescription);

        var features = DistinctNonEmpty(ServiceHeadings(pages));
        var voiceSamples = BuildVoiceSamples(pages, website, section);
        var ctaPhrases = ExtractCtaPhrases(pages);
        var audiences = ExtractAudiences(FirstParagraph(homepage), FirstParagraph(about));

        var notes = new List<string>
        {
            "Provisional kit from owned project-site crawl — review samples and Accept before write.",
        };
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
            KnowsAbout = features.Take(12).ToList(),
            AreaServed = [],
            SameAs = [],
            VoiceSamples = voiceSamples,
            VoiceGuidance = ["Provisional kit from project-site crawl — review samples and Accept before write."],
            VoiceStatus = "provisional",
            CtaPhrases = ctaPhrases,
            Notes = notes,
        };

        if (string.IsNullOrWhiteSpace(kit.CompanyName) && string.IsNullOrWhiteSpace(kit.Website))
        {
            throw new InvalidOperationException(
                "Brand kit from project-site crawl is empty (no company name or website) — refuse to write without site identity.");
        }

        _logger.LogInformation(
            "Built BrandKit from project-site run {RunId} ({PageCount} pages).",
            projectSiteCrawlRunId,
            pages.Count);

        return kit;
    }

    private async Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> LoadAllPagesAsync(Guid runId, CancellationToken ct)
    {
        var all = new List<GccV2ProjectSiteCrawlPageDto>();
        var offset = 0;
        const int batch = 100;
        while (true)
        {
            var chunk = await _repo.ListProjectSiteCrawlPagesAsync(runId, batch, offset, ct);
            if (chunk.Count == 0) break;
            all.AddRange(chunk);
            if (chunk.Count < batch) break;
            offset += chunk.Count;
        }

        return all;
    }

    private static IReadOnlyList<string> BuildVoiceSamples(
        IReadOnlyList<GccV2ProjectSiteCrawlPageDto> pages,
        string? website,
        SiteSectionContextDto? section)
    {
        var preferred = PreferHighSignalPages(pages, website);
        var samples = new List<string>();
        var totalChars = 0;

        foreach (var page in preferred)
        {
            if (string.IsNullOrWhiteSpace(page.Html)) continue;
            var url = string.IsNullOrWhiteSpace(page.FinalUrl) ? page.Url : page.FinalUrl;
            var extracted = Partner.GccV2ArticleHtmlExtractor.Extract(url, page.Html);
            var excerpt = string.Join(" ", extracted.Paragraphs.Take(3));
            if (string.IsNullOrWhiteSpace(excerpt)) continue;

            excerpt = excerpt.Length > 900 ? excerpt[..900] : excerpt;
            samples.Add($"{url}\n{excerpt}");
            totalChars += excerpt.Length;
            if (totalChars >= 1000 && samples.Count >= 2) break;
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

    private static IEnumerable<GccV2ProjectSiteCrawlPageDto> PreferHighSignalPages(
        IReadOnlyList<GccV2ProjectSiteCrawlPageDto> pages,
        string? website)
    {
        static int Score(GccV2ProjectSiteCrawlPageDto p, string? site)
        {
            var url = (string.IsNullOrWhiteSpace(p.FinalUrl) ? p.Url : p.FinalUrl).ToLowerInvariant();
            var score = 0;
            if (IsHomepageUrl(url, site)) score += 100;
            if (url.Contains("/about")) score += 80;
            if (url.Contains("/company") || url.Contains("/who-we")) score += 60;
            if (url.Contains("/service") || url.Contains("/product") || url.Contains("/solution")) score += 40;
            if (!string.IsNullOrWhiteSpace(p.Html)) score += Math.Min(30, p.Html!.Length / 500);
            return score;
        }

        return pages
            .Where(p => !string.IsNullOrWhiteSpace(p.Url))
            .OrderByDescending(p => Score(p, website));
    }

    private static IReadOnlyList<string> ExtractCtaPhrases(IReadOnlyList<GccV2ProjectSiteCrawlPageDto> pages)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Html)) continue;
            var roots = GccV2HeadingTreeBuilder.Build(page.Html);
            CollectLinkTexts(roots, counts);
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .Take(12)
            .ToList();
    }

    private static void CollectLinkTexts(IReadOnlyList<GccV2HeadingNode> nodes, Dictionary<string, int> counts)
    {
        foreach (var node in nodes)
        {
            if (node.Links is not null)
            {
                foreach (var link in node.Links)
                {
                    var text = (link.Text ?? "").Trim();
                    if (text.Length is < 2 or > 60) continue;
                    if (text.Contains("http", StringComparison.OrdinalIgnoreCase)) continue;
                    counts[text] = counts.TryGetValue(text, out var n) ? n + 1 : 1;
                }
            }

            if (node.Children is { Count: > 0 })
                CollectLinkTexts(node.Children, counts);
        }
    }

    private static IReadOnlyList<string> ExtractAudiences(string? homepageText, string? aboutText)
    {
        var found = new List<string>();
        foreach (var text in new[] { homepageText, aboutText })
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

    private static IReadOnlyList<string> ServiceHeadings(IReadOnlyList<GccV2ProjectSiteCrawlPageDto> pages)
    {
        var headings = new List<string>();
        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Html)) continue;
            var url = (page.FinalUrl ?? page.Url).ToLowerInvariant();
            if (!url.Contains("/service") && !url.Contains("/product") && !url.Contains("/solution"))
                continue;

            foreach (var node in GccV2HeadingTreeBuilder.Build(page.Html))
                CollectHeadingTexts(node, headings);
        }

        return DistinctNonEmpty(headings).Take(12).ToList();
    }

    private static void CollectHeadingTexts(GccV2HeadingNode node, List<string> headings)
    {
        if (!string.IsNullOrWhiteSpace(node.HeadingText) && node.HeadingText.Length > 3)
            headings.Add(node.HeadingText.Trim());
        if (node.Children is null) return;
        foreach (var child in node.Children)
            CollectHeadingTexts(child, headings);
    }

    private static GccV2ProjectSiteCrawlPageDto? PickHomepage(
        IReadOnlyList<GccV2ProjectSiteCrawlPageDto> pages,
        string? website) =>
        pages.FirstOrDefault(p => IsHomepageUrl(p.FinalUrl ?? p.Url, website))
        ?? pages.FirstOrDefault();

    private static GccV2ProjectSiteCrawlPageDto? PickAbout(IReadOnlyList<GccV2ProjectSiteCrawlPageDto> pages) =>
        pages.FirstOrDefault(p =>
        {
            var url = (p.FinalUrl ?? p.Url).ToLowerInvariant();
            return url.Contains("/about") || url.Contains("/company") || url.Contains("/who-we");
        });

    private static string? HomepageTitle(GccV2ProjectSiteCrawlPageDto? page)
    {
        if (page is null || string.IsNullOrWhiteSpace(page.Html)) return null;
        var url = page.FinalUrl ?? page.Url;
        return Partner.GccV2ArticleHtmlExtractor.Extract(url, page.Html).Title;
    }

    private static string? FirstHeading(GccV2ProjectSiteCrawlPageDto? page)
    {
        if (page is null || string.IsNullOrWhiteSpace(page.Html)) return null;
        var url = page.FinalUrl ?? page.Url;
        return Partner.GccV2ArticleHtmlExtractor.Extract(url, page.Html).Headings.FirstOrDefault()?.Text;
    }

    private static string? FirstParagraph(GccV2ProjectSiteCrawlPageDto? page)
    {
        if (page is null || string.IsNullOrWhiteSpace(page.Html)) return null;
        var url = page.FinalUrl ?? page.Url;
        return Partner.GccV2ArticleHtmlExtractor.Extract(url, page.Html).Paragraphs.FirstOrDefault();
    }

    private static string? HomepageUrl(IReadOnlyList<GccV2ProjectSiteCrawlPageDto> pages) =>
        pages.Select(p => p.FinalUrl ?? p.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

    private static bool IsHomepageUrl(string? url, string? website)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (string.IsNullOrWhiteSpace(website)) return url.TrimEnd('/').Count(c => c == '/') <= 2;
        try
        {
            var u = new Uri(url);
            var w = new Uri(website.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? website : "https://" + website);
            return string.Equals(u.Host, w.Host, StringComparison.OrdinalIgnoreCase)
                   && (u.AbsolutePath == "/" || string.IsNullOrWhiteSpace(u.AbsolutePath));
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static string? NormalizeWebsite(string? siteUrl)
    {
        if (string.IsNullOrWhiteSpace(siteUrl)) return null;
        return siteUrl.Trim().TrimEnd('/');
    }

    private static string? HostFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var uri = new Uri(url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url);
            return uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static string? CleanTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static IReadOnlyList<string> DistinctNonEmpty(IEnumerable<string?> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
