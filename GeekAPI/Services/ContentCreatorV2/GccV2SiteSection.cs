using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.GeekSeo;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2;

public sealed record RelatedPageDto(string Url, string Title, HeadingDto[] Headings, string Excerpt);

public sealed record SiteSectionContextDto(
    [property: JsonPropertyName("siteAnalysisProfileId")] Guid SiteAnalysisId,
    string GapTopic,
    string? GapSectionPath,
    IReadOnlyList<RelatedPageDto> RelatedPages,
    IReadOnlyList<string> TopicalNeighbors,
    InformationGainNote? InformationGain = null);

public sealed record ContentGapDto(
    string Id,
    string Topic,
    string? SectionPath,
    string Reason,
    IReadOnlyList<string>? Hierarchy = null,
    string? SourcePageUrl = null);

public sealed record SiteAnalysisStoredPayload(
    IReadOnlyList<ContentGapDto> Gaps,
    IReadOnlyList<RelatedPageDto> SitePages,
    IReadOnlyList<string> TopicalNeighbors,
    Guid? SeoProfileId = null,
    Guid? SeoProjectId = null);

/// <summary>
/// Site section DTOs and gates copied from v1 <c>GccGenerateService</c> for v2-only orchestration.
/// </summary>
public static class GccV2SiteSection
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static SiteSectionContextDto? ParseSiteSection(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SiteSectionContextDto>(json, JsonOpts)
                ?? throw new InvalidOperationException("Site section JSON deserialized to null.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Site section JSON could not be parsed.", ex);
        }
    }

    public static void ValidateSiteSectionGate(Guid? siteAnalysisProfileId, SiteSectionContextDto? section)
    {
        if (siteAnalysisProfileId is null || siteAnalysisProfileId == Guid.Empty)
            throw new InvalidOperationException(
                "site analysis required — run or reuse an analysis for this domain");

        if (section is null) return;
        if (section.RelatedPages is null || section.RelatedPages.Count == 0)
            throw new InvalidOperationException(
                "Site Analyzer–started Generate requires non-empty relatedPages in site section context.");
    }

    public static IEnumerable<HttpGeekSeoSiteAnalyzerClient.PageSectionDto> FlattenSections(
        IEnumerable<HttpGeekSeoSiteAnalyzerClient.PageSectionDto> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node.Children is null) continue;
            foreach (var child in FlattenSections(node.Children))
                yield return child;
        }
    }

    public static SiteSectionContextDto? TryBuildSectionContext(
        Guid analysisId,
        SiteAnalysisStoredPayload payload,
        string gapTopic)
    {
        if (string.IsNullOrWhiteSpace(gapTopic)) return null;

        var gap = payload.Gaps.FirstOrDefault(g =>
            string.Equals(g.Topic, gapTopic, StringComparison.OrdinalIgnoreCase));
        var sectionPath = gap?.SectionPath;

        IEnumerable<RelatedPageDto> candidates = payload.SitePages
            .Where(p => !string.IsNullOrWhiteSpace(p.Url));

        if (!string.IsNullOrWhiteSpace(sectionPath))
        {
            candidates = candidates.Where(p =>
                string.Equals(p.Title, sectionPath, StringComparison.OrdinalIgnoreCase)
                || (p.Excerpt?.Contains(sectionPath, StringComparison.OrdinalIgnoreCase) ?? false)
                || p.Headings.Any(h =>
                    h.Text.Contains(sectionPath, StringComparison.OrdinalIgnoreCase)));
        }

        var related = candidates
            .GroupBy(p => p.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(12)
            .ToList();

        if (related.Count == 0) return null;
        if (payload.TopicalNeighbors.Count == 0) return null;

        var informationGain = BuildPartialInformationGain(gapTopic, related);

        return new SiteSectionContextDto(
            analysisId,
            gapTopic,
            sectionPath,
            related,
            payload.TopicalNeighbors,
            informationGain);
    }

    public static bool HrefLooksLikeOnSiteToolPage(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;
        try
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var abs))
                return abs.AbsolutePath.Contains("/tools/", StringComparison.OrdinalIgnoreCase);
            return href.Contains("/tools/", StringComparison.OrdinalIgnoreCase);
        }
        catch (UriFormatException)
        {
            return href.Contains("/tools/", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static InformationGainNote BuildPartialInformationGain(
        string gapTopic,
        IReadOnlyList<RelatedPageDto> relatedPages)
    {
        var covers = new List<string>();
        foreach (var page in relatedPages.Take(12))
        {
            var bits = new List<string> { page.Title };
            bits.AddRange(page.Headings.Take(4).Select(h => h.Text));
            if (!string.IsNullOrWhiteSpace(page.Excerpt))
                bits.Add(Truncate(page.Excerpt, 120));
            covers.Add($"{page.Url}: {string.Join(" · ", bits.Where(b => !string.IsNullOrWhiteSpace(b)).Distinct())}");
        }

        var summary = covers.Count == 0
            ? $"No related site pages resolved for “{gapTopic}” — Information Gain needs section context."
            : $"This site covers {covers.Count} related page(s) near “{gapTopic}”. Upload a saved SERP to compare competitor opens.";

        return new InformationGainNote(covers, [], summary);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
