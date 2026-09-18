using System.Security.Cryptography;
using System.Text;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Library block-text verify for partner extraction assets (partner-extraction §2 ship rules + Appendix C).
/// </summary>
public static class GccV2PartnerExtractionVerify
{
    /// <summary>
    /// Re-verify citables (and claim-bearing assets) against GET /v1/pages block text.
    /// Assets without pageId stay paragraph-grounded only (<see cref="GccPartnerExtractionProvenance.QuoteVerified"/> = false).
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
        var pageTextCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        async Task<string?> LoadPageText(string? pageId, string? runId)
        {
            if (string.IsNullOrWhiteSpace(pageId)) return null;
            var cacheKey = $"{runId}|{pageId}";
            if (pageTextCache.TryGetValue(cacheKey, out var cached)) return cached;
            var page = await rag.GetPageTextAsync(pageId, ct, runId).ConfigureAwait(false);
            var pageText = page?.Text;
            pageTextCache[cacheKey] = pageText;
            return pageText;
        }

        var citables = new List<GccPartnerCitableAsset>(extraction.Citables.Count);
        foreach (var citable in extraction.Citables)
        {
            citables.Add(await VerifyCitableAsync(citable, LoadPageText, overrides, ct).ConfigureAwait(false));
        }

        var faqs = new List<GccPartnerFaqAsset>(extraction.FaqBank.Count);
        foreach (var faq in extraction.FaqBank)
        {
            var pageText = await LoadPageText(faq.Provenance.PageId, faq.Provenance.RunId).ConfigureAwait(false);
            faqs.Add(faq with
            {
                Provenance = StampProvenance(faq.Provenance, faq.VerifiedAnswer, pageText, overrides),
            });
        }

        // Proof is three addressable types now; each verifies against its own claim-bearing text.
        var caseStudies = new List<GccPartnerCaseStudyAsset>(extraction.CaseStudies.Count);
        foreach (var study in extraction.CaseStudies)
        {
            var pageText = await LoadPageText(study.Provenance.PageId, study.Provenance.RunId).ConfigureAwait(false);
            caseStudies.Add(study with
            {
                Provenance = StampProvenance(study.Provenance, study.OutcomeClaim, pageText, overrides),
            });
        }

        var testimonials = new List<GccPartnerTestimonialAsset>(extraction.Testimonials.Count);
        foreach (var testimonial in extraction.Testimonials)
        {
            var pageText = await LoadPageText(testimonial.Provenance.PageId, testimonial.Provenance.RunId).ConfigureAwait(false);
            testimonials.Add(testimonial with
            {
                Provenance = StampProvenance(testimonial.Provenance, testimonial.QuoteText, pageText, overrides),
            });
        }

        var awards = new List<GccPartnerAwardAsset>(extraction.Awards.Count);
        foreach (var award in extraction.Awards)
        {
            var pageText = await LoadPageText(award.Provenance.PageId, award.Provenance.RunId).ConfigureAwait(false);
            awards.Add(award with
            {
                Provenance = StampProvenance(award.Provenance, award.AwardName, pageText, overrides),
            });
        }

        var features = new List<GccPartnerFeatureAsset>(extraction.FeatureInventory.Count);
        foreach (var feature in extraction.FeatureInventory)
        {
            var pageText = await LoadPageText(feature.Provenance.PageId, feature.Provenance.RunId).ConfigureAwait(false);
            features.Add(feature with
            {
                Provenance = StampProvenance(feature.Provenance, feature.FeatureName, pageText, overrides),
            });
        }

        var constraints = new List<GccPartnerTechnicalConstraintAsset>(extraction.TechnicalConstraints.Count);
        foreach (var constraint in extraction.TechnicalConstraints)
        {
            var pageText = await LoadPageText(constraint.Provenance.PageId, constraint.Provenance.RunId).ConfigureAwait(false);
            constraints.Add(constraint with
            {
                Provenance = StampProvenance(constraint.Provenance, constraint.LimitText, pageText, overrides),
            });
        }

        var pricing = new List<GccPartnerPricingTierAsset>(extraction.PricingCatalog.Count);
        foreach (var tier in extraction.PricingCatalog)
        {
            var claim = tier.ListPrice is { } price
                ? $"{tier.TierName} {tier.PriceCurrency} {price}"
                : tier.TierName;
            var pageText = await LoadPageText(tier.Provenance.PageId, tier.Provenance.RunId).ConfigureAwait(false);
            // Price claims verify against the paragraph-grounded feature/trial text when present.
            var quote = !string.IsNullOrWhiteSpace(tier.FeatureGates) ? tier.FeatureGates
                : !string.IsNullOrWhiteSpace(tier.FreeOrTrial) ? tier.FreeOrTrial
                : claim;
            pricing.Add(tier with
            {
                Provenance = StampProvenance(tier.Provenance, quote ?? claim, pageText, overrides),
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

    /// <summary>Compute quote offsets in the page text; returns nulls when not found.</summary>
    public static (int? Start, int? End) FindQuoteOffsets(string pageText, string quote)
    {
        if (string.IsNullOrWhiteSpace(pageText) || string.IsNullOrWhiteSpace(quote))
            return (null, null);

        var idx = pageText.IndexOf(quote, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            var stripped = quote.Trim().Trim('"').Trim('\'');
            idx = pageText.IndexOf(stripped, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return (null, null);
            return (idx, idx + stripped.Length);
        }

        return (idx, idx + quote.Length);
    }

    public static string ComputeSourceDigest(string pageText)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pageText));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<GccPartnerCitableAsset> VerifyCitableAsync(
        GccPartnerCitableAsset citable,
        Func<string?, string?, Task<string?>> loadPageText,
        IReadOnlyDictionary<string, string> overrides,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pageText = await loadPageText(citable.Provenance.PageId, citable.Provenance.RunId).ConfigureAwait(false);
        var stamped = StampProvenance(citable.Provenance, citable.IsolatedClaim, pageText, overrides);
        return citable with { Provenance = stamped };
    }

    private static GccPartnerExtractionProvenance StampProvenance(
        GccPartnerExtractionProvenance provenance,
        string quote,
        string? pageText,
        IReadOnlyDictionary<string, string> overrides)
    {
        var rights = ResolveSourceRights(provenance, overrides);
        if (string.IsNullOrWhiteSpace(pageText)
            || !GccV2ToolResearchExtractor.IsVerbatimFromPage(quote, pageText))
        {
            return provenance with
            {
                Quote = quote,
                StartChar = null,
                EndChar = null,
                SourceRights = rights,
                QuoteVerified = false,
                SourceDigest = provenance.SourceDigest,
            };
        }

        var (start, end) = FindQuoteOffsets(pageText, quote);
        var digest = string.IsNullOrWhiteSpace(provenance.SourceDigest)
            ? ComputeSourceDigest(pageText)
            : provenance.SourceDigest;

        return provenance with
        {
            Quote = quote,
            StartChar = start,
            EndChar = end,
            SourceRights = rights,
            QuoteVerified = true,
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
