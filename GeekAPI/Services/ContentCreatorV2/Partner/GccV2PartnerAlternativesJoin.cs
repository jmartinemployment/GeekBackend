using System.Text.RegularExpressions;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Joins competitor deficits → partner recommended swaps (partner-extraction §5 honesty).
/// Never labels competitor pages as <c>crawlType:"partner"</c>.
/// </summary>
public static partial class GccV2PartnerAlternativesJoin
{
    public static GccPartnerExtractionDocument EnrichWithCompetitorDeficits(
        GccPartnerExtractionDocument extraction,
        IReadOnlyList<GccQuoteablePage> competitorPages,
        IReadOnlyList<string> partnerToolNames)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(competitorPages);
        if (competitorPages.Count == 0 || partnerToolNames.Count == 0)
            return extraction;

        var swaps = partnerToolNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        if (swaps.Count == 0) return extraction;

        var joined = new List<GccPartnerAlternativesAsset>(extraction.Alternatives);
        foreach (var page in competitorPages)
        {
            foreach (var paragraph in page.Paragraphs)
            {
                if (!DeficitHintRegex().IsMatch(paragraph)) continue;
                var deficit = Truncate(NormalizeWhitespace(paragraph), 240);
                if (deficit.Length < 20) continue;

                var provenance = new GccPartnerExtractionProvenance(
                    page.Url,
                    GccPartnerExtractionDocument.CrawlTypeCompetitors,
                    page.RunId,
                    page.PageId,
                    page.SectionTitle,
                    page.SourceDigest,
                    page.CrawledAtUtc,
                    Quote: deficit);

                var pivot = Truncate(
                    $"When facing this rival limitation — {Truncate(deficit, 100)} — consider partner tools: {string.Join(", ", swaps)}.",
                    320);

                joined.Add(new GccPartnerAlternativesAsset(deficit, swaps, pivot, provenance));
            }
        }

        var deduped = joined
            .GroupBy(a => NormalizeWhitespace(a.TriggerDeficit), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(40)
            .ToList();

        return extraction with { Alternatives = deduped };
    }

    private static string NormalizeWhitespace(string value) =>
        WhitespaceRegex().Replace(value.Trim(), " ");

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value[..max].TrimEnd() + "…";
    }

    [GeneratedRegex(@"\b(lacks?|does not (include|support)|missing|limited to|no native|not available|cannot|expensive|restrictive)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeficitHintRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
