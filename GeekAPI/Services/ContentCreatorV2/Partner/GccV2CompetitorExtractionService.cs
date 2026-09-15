using System.Text.RegularExpressions;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Deterministic (regex/heuristic) competitor library extraction (competitor-extraction plan §5–§7).
/// This IS the live production path — called from
/// <see cref="GeekCrawler.GccV2GeekCrawlerResearchResolver.MergeCompetitorResearchAsync"/>.
/// No <c>ContentCreatorV2.Synthesis</c> namespace exists in this codebase; do not describe this
/// service as superseded by one. <see cref="IGccV2SchemaConstrainedGenerator"/>
/// (<c>Generation/GccV2SchemaConstrainedGenerator.cs</c>) is a real, separate, not-yet-wired seam
/// for a future schema-constrained rewrite — it has zero current callers and does not replace this.
/// Always stamps <c>crawlType:"competitors"</c>. Never invents soft success without grounded text.
/// A competitor entity must never carry a sell/CTA/offer field — competitors are rival businesses,
/// not products this operator sells (see plans/competitor-extraction-complete.md §1 and
/// plans/rag-foundation-rewrite.md §0.000).
/// </summary>
public static partial class GccV2CompetitorExtractionService
{
    public static GccCompetitorExtractionDocument ExtractFromPages(
        IReadOnlyList<GccQuoteablePage> pages,
        IReadOnlyList<string>? partnerToolNames = null,
        IReadOnlyList<string>? competitorSeedUrls = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
            return EmptyDocument();

        // Shared min-expand heuristics (same patterns as partner) then relabel crawlType.
        var mirrored = GccV2PartnerExtractionService.ExtractFromPages(pages, partnerToolNames);
        var swaps = (partnerToolNames ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        var gapMap = new List<GccCompetitorGapMapAsset>();
        var framing = new List<GccCompetitorFramingAsset>();
        var demand = new List<GccCompetitorDemandSignalAsset>();
        var typeLabels = new List<GccCompetitorTypeLabelAsset>();
        var deficits = new List<GccCompetitorDeficitRouterAsset>();
        var claimRisk = new List<GccCompetitorClaimRiskAsset>();

        foreach (var page in pages)
        {
            var provenance = Relabel(BuildBaseProvenance(page));
            ExtractGapMap(page, provenance, gapMap);
            ExtractFraming(page, provenance, framing);
            ExtractDemand(page, provenance, demand);
            ExtractTypeLabel(page, provenance, competitorSeedUrls, typeLabels);
            ExtractDeficits(page, provenance, swaps, deficits);
            ExtractClaimRisk(page, provenance, claimRisk);
        }

        // Partner-side alternatives that came from competitor-like deficit language on these pages
        // also feed the deficit router when partner swaps are known.
        foreach (var alt in mirrored.Alternatives)
        {
            if (swaps.Count == 0) break;
            deficits.Add(new GccCompetitorDeficitRouterAsset(
                alt.TriggerDeficit,
                alt.Provenance.OriginProofUrl,
                swaps,
                alt.PivotCopy,
                Relabel(alt.Provenance)));
        }

        return new GccCompetitorExtractionDocument(
            GccCompetitorExtractionDocument.CurrentExtractorVersion,
            mirrored.PricingCatalog.Select(p => new GccCompetitorPricingTierAsset(
                p.TierName, p.ListPrice, p.PriceCurrency, p.BillingPeriod, p.OverageTerms,
                p.OriginProofUrl, Relabel(p.Provenance))).ToList(),
            mirrored.FaqBank.Select(f => new GccCompetitorFaqAsset(
                f.Question, f.VerifiedAnswer, f.OriginProofUrl, Relabel(f.Provenance))).ToList(),
            mirrored.ProofPack.Select(p => new GccCompetitorProofAsset(
                p.ProofKind, p.ProofClaim, p.OriginProofUrl, Relabel(p.Provenance))).ToList(),
            mirrored.Disqualifiers.Select(d => new GccCompetitorDisqualifierAsset(
                d.LimitType, d.LimitDetail, d.OriginProofUrl, Relabel(d.Provenance))).ToList(),
            Dedup(gapMap, g => g.GapTopic),
            Dedup(framing, f => f.FrameExcerpt),
            Dedup(demand, d => d.PrimaryKeywordFocus + "|" + d.ContentFormat),
            Dedup(typeLabels, t => t.EntityName + "|" + t.PrimaryUrl),
            Dedup(deficits, d => d.TriggerDeficit),
            mirrored.Comparisons.Select(c => new GccCompetitorComparisonAxisAsset(
                c.StandardizedFeatureId,
                c.CapabilityPayload,
                c.Provenance.OriginProofUrl,
                Relabel(c.Provenance))).ToList(),
            Dedup(claimRisk, c => c.ClaimText));
    }

    public static GccCompetitorExtractionDocument EmptyDocument() =>
        new(
            GccCompetitorExtractionDocument.CurrentExtractorVersion,
            [], [], [], [], [], [], [], [], [], [], []);

    private static void ExtractGapMap(
        GccQuoteablePage page,
        GccPartnerExtractionProvenance provenance,
        List<GccCompetitorGapMapAsset> sink)
    {
        foreach (var heading in page.Headings.Where(h => h.Level is >= 2 and <= 3))
        {
            var topic = Truncate(Normalize(heading.Text), 120);
            if (topic.Length < 8) continue;
            var relatedParas = page.Paragraphs
                .Where(p => p.Contains(topic, StringComparison.OrdinalIgnoreCase)
                            || topic.Contains(Truncate(p, 40), StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            var depth = relatedParas.Count == 0 ? "missing"
                : relatedParas.Sum(p => p.Length) < 120 ? "thin"
                : AsOfRegex().IsMatch(string.Join(' ', relatedParas)) ? "outdated"
                : "thin";
            if (depth == "thin" && relatedParas.Sum(p => p.Length) >= 240)
                continue;

            sink.Add(new GccCompetitorGapMapAsset(
                topic,
                depth,
                $"Cover {topic} with more depth than the rival page.",
                provenance));
        }
    }

    private static void ExtractFraming(
        GccQuoteablePage page,
        GccPartnerExtractionProvenance provenance,
        List<GccCompetitorFramingAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!UnlikeRegex().IsMatch(paragraph) && !VsRegex().IsMatch(paragraph)) continue;
            var excerpt = Truncate(Normalize(paragraph), 240);
            var sentiment = paragraph.Contains("unlike", StringComparison.OrdinalIgnoreCase)
                            || paragraph.Contains("don't", StringComparison.OrdinalIgnoreCase)
                ? "dismissive"
                : "neutral";
            sink.Add(new GccCompetitorFramingAsset(
                "us_vs_them",
                excerpt,
                sentiment,
                page.Url,
                provenance with { Quote = excerpt }));
        }
    }

    private static void ExtractDemand(
        GccQuoteablePage page,
        GccPartnerExtractionProvenance provenance,
        List<GccCompetitorDemandSignalAsset> sink)
    {
        var hierarchy = page.Headings
            .Where(h => h.Level is >= 2 and <= 3)
            .Select(h => Truncate(Normalize(h.Text), 120))
            .Where(t => t.Length > 0)
            .Take(20)
            .ToList();
        var keyword = !string.IsNullOrWhiteSpace(page.Title) ? Truncate(page.Title!, 120) : hierarchy.FirstOrDefault();
        var format = page.Url.Contains("/blog", StringComparison.OrdinalIgnoreCase) ? "blog"
            : page.Url.Contains("/docs", StringComparison.OrdinalIgnoreCase) ? "docs"
            : page.Url.Contains("/pricing", StringComparison.OrdinalIgnoreCase) ? "landing"
            : hierarchy.Count > 0 ? "landing"
            : "other";
        var theme = page.Paragraphs.FirstOrDefault(p =>
            p.Length is >= 24 and <= 160
            && (p.Contains("replace", StringComparison.OrdinalIgnoreCase)
                || p.Contains("best", StringComparison.OrdinalIgnoreCase)
                || p.Contains("automate", StringComparison.OrdinalIgnoreCase)));
        var intent = format == "docs" ? "Informational"
            : page.Url.Contains("pricing", StringComparison.OrdinalIgnoreCase)
              || page.Url.Contains("demo", StringComparison.OrdinalIgnoreCase)
                ? "Transactional"
                : "Commercial Investigation";

        sink.Add(new GccCompetitorDemandSignalAsset(
            keyword,
            format,
            intent,
            theme is null ? null : Truncate(Normalize(theme), 160),
            provenance));
    }

    private static void ExtractTypeLabel(
        GccQuoteablePage page,
        GccPartnerExtractionProvenance provenance,
        IReadOnlyList<string>? seedUrls,
        List<GccCompetitorTypeLabelAsset> sink)
    {
        var entity = string.IsNullOrWhiteSpace(page.Title)
            ? (Uri.TryCreate(page.Url, UriKind.Absolute, out var u) ? u.Host : page.Url)
            : page.Title!;
        var text = string.Join('\n', page.Paragraphs);
        var sellsProduct = ProductHintRegex().IsMatch(text)
                           || page.Url.Contains("pricing", StringComparison.OrdinalIgnoreCase);
        var contentOnly = page.Url.Contains("/blog", StringComparison.OrdinalIgnoreCase)
                          || AuthorityHintRegex().IsMatch(text);
        var type = sellsProduct && contentOnly ? "both"
            : sellsProduct ? "direct"
            : contentOnly ? "content"
            : "direct";
        var rationale = type switch
        {
            "content" => "Coverage/editorial posture without clear product pricing sell.",
            "both" => "Sells product category and publishes broad coverage content.",
            _ => "Sells same category product signals (pricing/features).",
        };
        var primary = seedUrls?.FirstOrDefault(s =>
                          Uri.TryCreate(s, UriKind.Absolute, out var seedUri)
                          && Uri.TryCreate(page.Url, UriKind.Absolute, out var pageUri)
                          && string.Equals(seedUri.Host, pageUri.Host, StringComparison.OrdinalIgnoreCase))
                      ?? page.Url;

        sink.Add(new GccCompetitorTypeLabelAsset(type, rationale, Truncate(entity, 120), primary, provenance));
    }

    private static void ExtractDeficits(
        GccQuoteablePage page,
        GccPartnerExtractionProvenance provenance,
        IReadOnlyList<string> swaps,
        List<GccCompetitorDeficitRouterAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            if (!DeficitHintRegex().IsMatch(paragraph)) continue;
            var deficit = Truncate(Normalize(paragraph), 240);
            if (deficit.Length < 20) continue;
            var pivot = swaps.Count == 0
                ? null
                : Truncate(
                    $"If you need an alternative when facing this rival limit — {Truncate(deficit, 100)} — consider {string.Join(", ", swaps)}.",
                    320);
            sink.Add(new GccCompetitorDeficitRouterAsset(
                deficit,
                page.Url,
                swaps,
                pivot,
                provenance with { Quote = deficit }));
        }
    }

    private static void ExtractClaimRisk(
        GccQuoteablePage page,
        GccPartnerExtractionProvenance provenance,
        List<GccCompetitorClaimRiskAsset> sink)
    {
        foreach (var paragraph in page.Paragraphs)
        {
            string? kind = null;
            if (SuperlativeRegex().IsMatch(paragraph)) kind = "superlative";
            else if (AbsoluteRegex().IsMatch(paragraph)) kind = "absolute";
            else if (AsOfRegex().IsMatch(paragraph) && paragraph.Length < 180) kind = "dated";
            if (kind is null) continue;
            var claim = Truncate(Normalize(paragraph), 200);
            sink.Add(new GccCompetitorClaimRiskAsset(
                claim,
                kind,
                page.Url,
                "do_not_echo_as_fact",
                provenance with { Quote = claim }));
        }
    }

    private static GccPartnerExtractionProvenance BuildBaseProvenance(GccQuoteablePage page) =>
        new(
            page.Url,
            GccCompetitorExtractionDocument.CrawlTypeCompetitor,
            page.RunId,
            page.PageId,
            page.SectionTitle,
            page.SourceDigest,
            page.CrawledAtUtc);

    private static GccPartnerExtractionProvenance Relabel(GccPartnerExtractionProvenance provenance) =>
        provenance with { CrawlType = GccCompetitorExtractionDocument.CrawlTypeCompetitor };

    private static List<T> Dedup<T>(List<T> items, Func<T, string> key) =>
        items.GroupBy(key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(40).ToList();

    private static string Normalize(string value) => WhitespaceRegex().Replace(value.Trim(), " ");

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value[..max].TrimEnd() + "…";
    }

    [GeneratedRegex(@"\b(unlike|compared to|versus|vs\.?)\s+(?<name>[A-Z][\w.+-]*(?:\s+[A-Z][\w.+-]*){0,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnlikeRegex();

    [GeneratedRegex(@"\bvs\.?\s+(?<name>[A-Za-z][\w.+-]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VsRegex();

    [GeneratedRegex(@"\b(pricing|subscribe|buy|free trial|demo|software|platform|product)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductHintRegex();

    [GeneratedRegex(@"\b(editor|advisor|magazine|publication|journalist|guide to)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorityHintRegex();

    [GeneratedRegex(@"\b(lacks?|does not (include|support)|missing|limited to|no native|not available|cannot|expensive|restrictive)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeficitHintRegex();

    [GeneratedRegex(@"\b(#1|number one|best[- ]in[- ]class|world'?s leading|unmatched|unbeatable)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuperlativeRegex();

    [GeneratedRegex(@"\b(always|never|guaranteed|100%|everyone)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteRegex();

    [GeneratedRegex(@"\b(as of|effective)\s+(?<asof>[A-Za-z0-9,\s\-/]{4,40})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AsOfRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
