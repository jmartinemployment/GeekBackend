using System.Security.Cryptography;
using System.Text;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Library Markdown verify for partner extraction assets (partner-extraction §2 ship rules + Appendix C).
/// </summary>
public static class GccV2PartnerExtractionVerify
{
    /// <summary>
    /// Re-verify citables (and claim-bearing assets) against GET /v1/pages Markdown.
    /// Assets without pageId stay paragraph-grounded only (<see cref="GccPartnerExtractionProvenance.MarkdownVerified"/> = false).
    /// </summary>
    public static async Task<GccPartnerExtractionDocument> VerifyAgainstLibraryAsync(
        GccPartnerExtractionDocument extraction,
        IGeekCrawlerRagClient rag,
        string? rawBriefJson,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(rag);

        if (!rag.IsEnabled)
            return extraction;

        var overrides = GccV2SourceRightsGate.ParseBriefOverrides(rawBriefJson);
        var markdownCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        async Task<string?> LoadMarkdown(string? pageId, string? runId)
        {
            if (string.IsNullOrWhiteSpace(pageId)) return null;
            var cacheKey = $"{runId}|{pageId}";
            if (markdownCache.TryGetValue(cacheKey, out var cached)) return cached;
            var page = await rag.GetPageMarkdownAsync(pageId, ct, runId).ConfigureAwait(false);
            var md = page?.Markdown;
            markdownCache[cacheKey] = md;
            return md;
        }

        var citables = new List<GccPartnerCitableAsset>(extraction.Citables.Count);
        foreach (var citable in extraction.Citables)
        {
            citables.Add(await VerifyCitableAsync(citable, LoadMarkdown, overrides, ct).ConfigureAwait(false));
        }

        var faqs = new List<GccPartnerFaqAsset>(extraction.FaqBank.Count);
        foreach (var faq in extraction.FaqBank)
        {
            var md = await LoadMarkdown(faq.Provenance.PageId, faq.Provenance.RunId).ConfigureAwait(false);
            faqs.Add(faq with
            {
                Provenance = StampProvenance(faq.Provenance, faq.VerifiedAnswer, md, overrides),
            });
        }

        // Proof is three addressable types now; each verifies against its own claim-bearing text.
        var caseStudies = new List<GccPartnerCaseStudyAsset>(extraction.CaseStudies.Count);
        foreach (var study in extraction.CaseStudies)
        {
            var md = await LoadMarkdown(study.Provenance.PageId, study.Provenance.RunId).ConfigureAwait(false);
            caseStudies.Add(study with
            {
                Provenance = StampProvenance(study.Provenance, study.OutcomeClaim, md, overrides),
            });
        }

        var testimonials = new List<GccPartnerTestimonialAsset>(extraction.Testimonials.Count);
        foreach (var testimonial in extraction.Testimonials)
        {
            var md = await LoadMarkdown(testimonial.Provenance.PageId, testimonial.Provenance.RunId).ConfigureAwait(false);
            testimonials.Add(testimonial with
            {
                Provenance = StampProvenance(testimonial.Provenance, testimonial.QuoteText, md, overrides),
            });
        }

        var awards = new List<GccPartnerAwardAsset>(extraction.Awards.Count);
        foreach (var award in extraction.Awards)
        {
            var md = await LoadMarkdown(award.Provenance.PageId, award.Provenance.RunId).ConfigureAwait(false);
            awards.Add(award with
            {
                Provenance = StampProvenance(award.Provenance, award.AwardName, md, overrides),
            });
        }

        var features = new List<GccPartnerFeatureAsset>(extraction.FeatureInventory.Count);
        foreach (var feature in extraction.FeatureInventory)
        {
            var md = await LoadMarkdown(feature.Provenance.PageId, feature.Provenance.RunId).ConfigureAwait(false);
            features.Add(feature with
            {
                Provenance = StampProvenance(feature.Provenance, feature.FeatureName, md, overrides),
            });
        }

        var constraints = new List<GccPartnerTechnicalConstraintAsset>(extraction.TechnicalConstraints.Count);
        foreach (var constraint in extraction.TechnicalConstraints)
        {
            var md = await LoadMarkdown(constraint.Provenance.PageId, constraint.Provenance.RunId).ConfigureAwait(false);
            constraints.Add(constraint with
            {
                Provenance = StampProvenance(constraint.Provenance, constraint.LimitText, md, overrides),
            });
        }

        var pricing = new List<GccPartnerPricingTierAsset>(extraction.PricingCatalog.Count);
        foreach (var tier in extraction.PricingCatalog)
        {
            var claim = tier.ListPrice is { } price
                ? $"{tier.TierName} {tier.PriceCurrency} {price}"
                : tier.TierName;
            var md = await LoadMarkdown(tier.Provenance.PageId, tier.Provenance.RunId).ConfigureAwait(false);
            // Price claims verify against the paragraph-grounded feature/trial text when present.
            var quote = !string.IsNullOrWhiteSpace(tier.FeatureGates) ? tier.FeatureGates
                : !string.IsNullOrWhiteSpace(tier.FreeOrTrial) ? tier.FreeOrTrial
                : claim;
            pricing.Add(tier with
            {
                Provenance = StampProvenance(tier.Provenance, quote ?? claim, md, overrides),
            });
        }

        return extraction with
        {
            Citables = citables,
            FaqBank = faqs,
            CaseStudies = caseStudies,
            Testimonials = testimonials,
            Awards = awards,
            FeatureInventory = features,
            TechnicalConstraints = constraints,
            PricingCatalog = pricing,
        };
    }

    /// <summary>Compute quote offsets in Markdown; returns nulls when not found.</summary>
    public static (int? Start, int? End) FindQuoteOffsets(string markdown, string quote)
    {
        if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(quote))
            return (null, null);

        var idx = markdown.IndexOf(quote, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            var stripped = quote.Trim().Trim('"').Trim('\'');
            idx = markdown.IndexOf(stripped, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return (null, null);
            return (idx, idx + stripped.Length);
        }

        return (idx, idx + quote.Length);
    }

    public static string ComputeSourceDigest(string markdown)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(markdown));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<GccPartnerCitableAsset> VerifyCitableAsync(
        GccPartnerCitableAsset citable,
        Func<string?, string?, Task<string?>> loadMarkdown,
        IReadOnlyDictionary<string, string> overrides,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var md = await loadMarkdown(citable.Provenance.PageId, citable.Provenance.RunId).ConfigureAwait(false);
        var stamped = StampProvenance(citable.Provenance, citable.IsolatedClaim, md, overrides);
        return citable with { Provenance = stamped };
    }

    private static GccPartnerExtractionProvenance StampProvenance(
        GccPartnerExtractionProvenance provenance,
        string quote,
        string? markdown,
        IReadOnlyDictionary<string, string> overrides)
    {
        var rights = ResolveSourceRights(provenance, overrides);
        if (string.IsNullOrWhiteSpace(markdown)
            || !GccV2ToolResearchExtractor.IsVerbatimFromPage(quote, markdown))
        {
            return provenance with
            {
                Quote = quote,
                StartChar = null,
                EndChar = null,
                SourceRights = rights,
                MarkdownVerified = false,
                SourceDigest = provenance.SourceDigest,
            };
        }

        var (start, end) = FindQuoteOffsets(markdown, quote);
        var digest = string.IsNullOrWhiteSpace(provenance.SourceDigest)
            ? ComputeSourceDigest(markdown)
            : provenance.SourceDigest;

        return provenance with
        {
            Quote = quote,
            StartChar = start,
            EndChar = end,
            SourceRights = rights,
            MarkdownVerified = true,
            SourceDigest = digest,
        };
    }

    private static string ResolveSourceRights(
        GccPartnerExtractionProvenance provenance,
        IReadOnlyDictionary<string, string> overrides)
    {
        var pageId = (provenance.PageId ?? "").Trim();
        var runId = (provenance.RunId ?? "").Trim();
        if (overrides.Count > 0)
        {
            if (pageId.Length > 0 && runId.Length > 0
                && overrides.TryGetValue($"{runId}|{pageId}", out var both))
                return GccV2SourceRightsGate.Normalize(both);
            if (pageId.Length > 0 && overrides.TryGetValue(pageId, out var byPage))
                return GccV2SourceRightsGate.Normalize(byPage);
            if (runId.Length > 0 && overrides.TryGetValue(runId, out var byRun))
                return GccV2SourceRightsGate.Normalize(byRun);
        }

        return GccV2SourceRightsGate.Normalize(provenance.SourceRights);
    }
}
