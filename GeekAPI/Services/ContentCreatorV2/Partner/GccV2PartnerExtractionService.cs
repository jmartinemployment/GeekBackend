using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.Workflow.Providers;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>
/// Partner extraction — a third-party SaaS product promoted for affiliate revenue.
///
/// Replaces a 983-line regex extractor (36 <c>[GeneratedRegex]</c> methods, 123 references). Regex can
/// find a price string; it cannot identify "the three things this tool does better than anyone else",
/// which the previous <c>WinThemeHintRegex</c> approximated by taking the first sentence between 20 and
/// 220 characters matching a hint pattern. Those win themes are the persuasive backbone of every
/// recommendation (plans/rag-foundation-rewrite.md W2).
///
/// Extraction is schema-constrained via <see cref="IGccV2SchemaConstrainedGenerator"/> and every asset
/// carries the exact source quote, so <c>GccV2PartnerExtractionVerify</c> can check it against source
/// page text and stamp offsets, digest and rights. This is GeekAPI-side generation over page text that
/// RAG retrieved — RAG itself still only retrieves and verifies.
///
/// Fail-closed and silent: a page that yields nothing usable contributes nothing. No fallback path,
/// no fabricated fields.
/// </summary>
public sealed class GccV2PartnerExtractionService(
    IGccV2SchemaConstrainedGenerator generator,
    IContentProviderFactory providers,
    ILogger<GccV2PartnerExtractionService> logger)
{
    /// <summary>Sent as response_format.json_schema.name. OpenAI rejects anything outside
    /// [a-zA-Z0-9_-] with a 400, so this must not pick up the dotted ".v4" version style.</summary>
    public const string ProviderSchemaName = "partner-extraction-v4";

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            // Explicit resolver required: JsonSchemaExporter marks the options read-only, and
            // reflection-based resolution is not picked up implicitly - without this every call
            // throws "must specify a TypeInfoResolver setting before being marked as read-only".
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

    private const int MaxParagraphsPerPage = 40;
    private const int MaxParagraphChars = 1_200;

    private const string SystemPrompt = """
        You extract structured product intelligence about a PARTNER from one crawled web page.

        A partner is a third-party SaaS product that the operator promotes for affiliate revenue. It is
        schema.org SoftwareApplication. Subscription pricing, seat caps, feature matrices, integrations,
        API limits and billing cycles are all valid here.

        Never describe a partner as if it delivers human consulting or localised agency execution.

        Extract only what this page states. Omit any field the page does not support. Never infer a
        feature, never guess a price or region, never generalise from industry knowledge, never fill a
        gap with a plausible value. Returning an empty list is correct when the page does not cover that
        signal.

        Every item must carry "quote": the exact verbatim span from the page that supports it, copied
        character-for-character. An item whose quote is not literally present on the page is invalid.

        Specific rules:
        - featureInventory is the definitive list of what the product does. Do not put comparison
          statements here.
        - winThemes are differentiators the page itself claims; landmine is a competitor claim the page
          pre-empts; coachingLine is how the page answers it. Do not invent a landmine.
        - caseStudies need a named client. metricName/metricValue only when the page states them —
          never compute, convert or round a metric.
        - testimonials need the quoted text; attribution, role and industry only when stated.
        - awards: source must be one of g2, capterra, producthunt, trustpilot, other.
        - technicalConstraints: kind must be one of api_rate_limit, storage_cap, character_limit,
          seat_cap, other. Give limitValue/limitUnit only when a number is published.
        - pricing: unitCostAmount/unitCostBasis only for genuine per-unit charges ("$15 per seat",
          "$0.02 per credit"), never for a flat tier price.
        - affiliateDisclosures: capture the disclosure text and, when the page names one, the
          jurisdiction or policy regime it cites (for example "FTC 16 CFR Part 255", "EU P2B").
        """;

    public static GccPartnerExtractionDocument EmptyDocument() =>
        new(GccPartnerExtractionDocument.CurrentExtractorVersion,
            [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);

    /// <summary>
    /// Extract partner payloads from crawled partner pages. Returns an empty document when nothing
    /// usable is found; never throws.
    /// </summary>
    public async Task<GccPartnerExtractionDocument> ExtractFromPagesAsync(
        IReadOnlyList<GccQuoteablePage> pages,
        IReadOnlyList<string>? partnerToolNames,
        CancellationToken ct)
    {
        if (pages is null || pages.Count == 0) return EmptyDocument();

        IContentGenerationProvider provider;
        try
        {
            provider = providers.GetDefault();
        }
        catch (Exception cause)
        {
            logger.LogWarning(cause, "Partner extraction skipped: no content provider available.");
            return EmptyDocument();
        }

        var citables = new List<GccPartnerCitableAsset>();
        var ads = new List<GccPartnerAdvertisementAsset>();
        var comparisons = new List<GccPartnerComparisonAsset>();
        var alternatives = new List<GccPartnerAlternativesAsset>();
        var pricing = new List<GccPartnerPricingTierAsset>();
        var icp = new List<GccPartnerIcpAsset>();
        var integrations = new List<GccPartnerIntegrationAsset>();
        var faqs = new List<GccPartnerFaqAsset>();
        var caseStudies = new List<GccPartnerCaseStudyAsset>();
        var testimonials = new List<GccPartnerTestimonialAsset>();
        var awards = new List<GccPartnerAwardAsset>();
        var features = new List<GccPartnerFeatureAsset>();
        var constraints = new List<GccPartnerTechnicalConstraintAsset>();
        var offerCtas = new List<GccPartnerOfferCtaAsset>();
        var disqualifiers = new List<GccPartnerDisqualifierAsset>();
        var playbooks = new List<GccPartnerUseCasePlaybookAsset>();
        var categories = new List<GccPartnerCategoryAsset>();
        var freshness = new List<GccPartnerFreshnessAsset>();
        var battlecards = new List<GccPartnerBattlecardSliceAsset>();
        var demoBeats = new List<GccPartnerDemoBeatAsset>();
        var compliance = new List<GccPartnerComplianceSnippetAsset>();
        var disclosures = new List<GccPartnerAffiliateDisclosureAsset>();

        var schema = GccV2AdHocJsonSchema.For<PartnerPageExtraction>(JsonOpts);

        foreach (var page in pages)
        {
            var x = await ExtractOnePageAsync(page, partnerToolNames, provider, schema, ct)
                .ConfigureAwait(false);
            if (x is null) continue;

            GccPartnerExtractionProvenance Prov(string? quote) => new(
                OriginProofUrl: page.Url,
                CrawlType: GccPartnerExtractionDocument.CrawlTypePartner,
                RunId: page.RunId,
                PageId: page.PageId,
                SectionTitle: page.SectionTitle,
                SourceDigest: page.SourceDigest,
                TemporalAnchorUtc: page.CrawledAtUtc,
                Quote: string.IsNullOrWhiteSpace(quote) ? null : quote);

            foreach (var c in x.Citables ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.IsolatedClaim)) continue;
                citables.Add(new GccPartnerCitableAsset(c.IsolatedClaim, page.Url, Prov(c.Quote ?? c.IsolatedClaim)));
            }

            foreach (var a in x.Advertisements ?? [])
            {
                if (string.IsNullOrWhiteSpace(a.MarketingHook)) continue;
                ads.Add(new GccPartnerAdvertisementAsset(
                    a.MarketingHook, a.PainPointTrigger ?? "", a.CtaWrapper ?? "", Prov(a.Quote)));
            }

            foreach (var c in x.Comparisons ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.StandardizedFeatureId)
                    || string.IsNullOrWhiteSpace(c.CapabilityPayload)) continue;
                comparisons.Add(new GccPartnerComparisonAsset(
                    c.StandardizedFeatureId, c.CapabilityPayload, c.NormalizedCost, Prov(c.Quote)));
            }

            foreach (var a in x.Alternatives ?? [])
            {
                if (string.IsNullOrWhiteSpace(a.TriggerDeficit)) continue;
                alternatives.Add(new GccPartnerAlternativesAsset(
                    a.TriggerDeficit, a.RecommendedSwap ?? [], a.PivotCopy ?? "", Prov(a.Quote)));
            }

            foreach (var p in x.Pricing ?? [])
            {
                if (string.IsNullOrWhiteSpace(p.TierName)) continue;
                pricing.Add(new GccPartnerPricingTierAsset(
                    p.TierName, p.ListPrice, p.PriceCurrency, p.BillingPeriod, p.FeatureGates,
                    p.FreeOrTrial, p.OverageTerms, p.UnitCostAmount, p.UnitCostBasis,
                    page.Url, Prov(p.Quote)));
            }

            foreach (var i in x.Icp ?? [])
            {
                if ((i.ServedSegments?.Count ?? 0) == 0 && (i.Industries?.Count ?? 0) == 0) continue;
                icp.Add(new GccPartnerIcpAsset(
                    i.ServedSegments ?? [], i.ExcludedSegments ?? [], i.CompanySizeBand,
                    i.Industries ?? [], i.BuyerRoles ?? [], Prov(i.Quote)));
            }

            foreach (var i in x.Integrations ?? [])
            {
                if (string.IsNullOrWhiteSpace(i.IntegrationName)) continue;
                integrations.Add(new GccPartnerIntegrationAsset(
                    i.IntegrationName, i.IntegrationType, i.ApiOrSdk, Prov(i.Quote)));
            }

            foreach (var f in x.Faqs ?? [])
            {
                if (string.IsNullOrWhiteSpace(f.Question) || string.IsNullOrWhiteSpace(f.Answer)) continue;
                faqs.Add(new GccPartnerFaqAsset(f.Question, f.Answer, page.Url, Prov(f.Quote ?? f.Answer)));
            }

            foreach (var c in x.CaseStudies ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.ClientName) || string.IsNullOrWhiteSpace(c.OutcomeClaim))
                    continue;
                caseStudies.Add(new GccPartnerCaseStudyAsset(
                    c.ClientName, c.Sector, c.OutcomeClaim, c.MetricName, c.MetricValue,
                    page.Url, Prov(c.Quote ?? c.OutcomeClaim)));
            }

            foreach (var t in x.Testimonials ?? [])
            {
                if (string.IsNullOrWhiteSpace(t.QuoteText)) continue;
                testimonials.Add(new GccPartnerTestimonialAsset(
                    t.QuoteText, t.AttributedTo, t.BuyerRole, t.Industry,
                    page.Url, Prov(t.Quote ?? t.QuoteText)));
            }

            foreach (var a in x.Awards ?? [])
            {
                if (string.IsNullOrWhiteSpace(a.AwardName)) continue;
                awards.Add(new GccPartnerAwardAsset(
                    a.AwardName, NormalizeAwardSource(a.Source), a.AwardedFor, a.AwardedPeriod,
                    page.Url, Prov(a.Quote)));
            }

            foreach (var f in x.FeatureInventory ?? [])
            {
                if (string.IsNullOrWhiteSpace(f.FeatureName)) continue;
                features.Add(new GccPartnerFeatureAsset(
                    f.FeatureName, f.FeatureCategory, f.GatedToTier, page.Url, Prov(f.Quote)));
            }

            foreach (var c in x.TechnicalConstraints ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.LimitText)) continue;
                constraints.Add(new GccPartnerTechnicalConstraintAsset(
                    NormalizeConstraintKind(c.ConstraintKind), c.LimitValue, c.LimitUnit, c.LimitText,
                    page.Url, Prov(c.Quote ?? c.LimitText)));
            }

            foreach (var o in x.OfferCtas ?? [])
            {
                if (string.IsNullOrWhiteSpace(o.CtaLabel) || string.IsNullOrWhiteSpace(o.DestinationUrl))
                    continue;
                offerCtas.Add(new GccPartnerOfferCtaAsset(
                    o.CtaLabel, o.DestinationUrl, o.OfferType ?? "unstated", Prov(o.Quote)));
            }

            foreach (var d in x.Disqualifiers ?? [])
            {
                if (string.IsNullOrWhiteSpace(d.LimitDetail)) continue;
                disqualifiers.Add(new GccPartnerDisqualifierAsset(
                    d.LimitType ?? "unstated", d.LimitDetail, page.Url, Prov(d.Quote ?? d.LimitDetail)));
            }

            foreach (var u in x.UseCasePlaybooks ?? [])
            {
                if (string.IsNullOrWhiteSpace(u.JobToBeDone)) continue;
                playbooks.Add(new GccPartnerUseCasePlaybookAsset(
                    u.JobToBeDone, u.CitedStepsOrFeatures ?? [], Prov(u.Quote)));
            }

            foreach (var c in x.Categories ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.PrimaryCategory)) continue;
                categories.Add(new GccPartnerCategoryAsset(c.PrimaryCategory, c.VsCategoryLabel, Prov(c.Quote)));
            }

            foreach (var f in x.Freshness ?? [])
            {
                if (string.IsNullOrWhiteSpace(f.ChangeSummary)) continue;
                freshness.Add(new GccPartnerFreshnessAsset(
                    f.ChangeKind ?? "update", f.ChangeSummary, f.StatedAsOf, page.Url, Prov(f.Quote)));
            }

            foreach (var b in x.BattlecardSlices ?? [])
            {
                if (string.IsNullOrWhiteSpace(b.WinTheme)) continue;
                battlecards.Add(new GccPartnerBattlecardSliceAsset(
                    b.WinTheme, b.Landmine ?? "", b.CoachingLine ?? "", Prov(b.Quote ?? b.WinTheme)));
            }

            foreach (var d in x.DemoBeats ?? [])
            {
                if (string.IsNullOrWhiteSpace(d.BeatTitle) || string.IsNullOrWhiteSpace(d.BeatClaim))
                    continue;
                demoBeats.Add(new GccPartnerDemoBeatAsset(
                    d.BeatTitle, d.BeatClaim, page.Url, Prov(d.Quote ?? d.BeatClaim)));
            }

            foreach (var c in x.ComplianceSnippets ?? [])
            {
                if (string.IsNullOrWhiteSpace(c.TermText)) continue;
                compliance.Add(new GccPartnerComplianceSnippetAsset(
                    c.TermKind ?? "unstated", c.TermText, page.Url, Prov(c.Quote ?? c.TermText)));
            }

            foreach (var d in x.AffiliateDisclosures ?? [])
            {
                if (string.IsNullOrWhiteSpace(d.DisclosureText)) continue;
                disclosures.Add(new GccPartnerAffiliateDisclosureAsset(
                    d.DisclosureText, d.JurisdictionOrPolicy, page.Url,
                    Prov(d.Quote ?? d.DisclosureText)));
            }
        }

        return new GccPartnerExtractionDocument(
            GccPartnerExtractionDocument.CurrentExtractorVersion,
            Dedupe(citables, a => a.IsolatedClaim),
            Dedupe(ads, a => a.MarketingHook),
            Dedupe(comparisons, a => a.StandardizedFeatureId + "|" + a.CapabilityPayload),
            Dedupe(alternatives, a => a.TriggerDeficit),
            Dedupe(pricing, a => a.TierName),
            Dedupe(icp, a => string.Join(",", a.ServedSegments) + "|" + (a.CompanySizeBand ?? "")),
            Dedupe(integrations, a => a.IntegrationName),
            Dedupe(faqs, a => a.Question),
            Dedupe(caseStudies, a => a.ClientName + "|" + a.OutcomeClaim),
            Dedupe(testimonials, a => a.QuoteText),
            Dedupe(awards, a => a.Source + "|" + a.AwardName),
            Dedupe(features, a => a.FeatureName),
            Dedupe(constraints, a => a.ConstraintKind + "|" + a.LimitText),
            Dedupe(offerCtas, a => a.DestinationUrl),
            Dedupe(disqualifiers, a => a.LimitDetail),
            Dedupe(playbooks, a => a.JobToBeDone),
            Dedupe(categories, a => a.PrimaryCategory),
            Dedupe(freshness, a => a.ChangeSummary),
            Dedupe(battlecards, a => a.WinTheme),
            Dedupe(demoBeats, a => a.BeatTitle),
            Dedupe(compliance, a => a.TermKind + "|" + a.TermText),
            Dedupe(disclosures, a => a.DisclosureText));
    }

    /// <summary>
    /// True when <paramref name="claim"/> appears verbatim in <paramref name="source"/>, ignoring
    /// whitespace shape. Retained for callers that ground a claim before use; the authoritative check
    /// remains the quote verify pass.
    /// </summary>
    public static bool IsGrounded(string? claim, string? source)
    {
        if (string.IsNullOrWhiteSpace(claim) || string.IsNullOrWhiteSpace(source)) return false;
        return Collapse(source).Contains(Collapse(claim), StringComparison.OrdinalIgnoreCase);
    }

    private static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeAwardSource(string? source)
    {
        var normalized = (source ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "g2" or "g2crowd" or "g2.com" => "g2",
            "capterra" => "capterra",
            "producthunt" or "product hunt" => "producthunt",
            "trustpilot" => "trustpilot",
            _ => "other",
        };
    }

    private static string NormalizeConstraintKind(string? kind)
    {
        var normalized = (kind ?? "").Trim().ToLowerInvariant().Replace(' ', '_');
        return normalized is "api_rate_limit" or "storage_cap" or "character_limit" or "seat_cap"
            ? normalized
            : "other";
    }

    private async Task<PartnerPageExtraction?> ExtractOnePageAsync(
        GccQuoteablePage page,
        IReadOnlyList<string>? partnerToolNames,
        IContentGenerationProvider provider,
        string schema,
        CancellationToken ct)
    {
        var userPrompt = BuildUserPrompt(page, partnerToolNames);
        if (userPrompt.Length == 0) return null;

        try
        {
            var completion = await generator.CompleteAsync<PartnerPageExtraction>(
                new GccV2SchemaConstrainedRequest(
                    SystemPrompt: SystemPrompt,
                    UserPrompt: userPrompt,
                    JsonSchema: schema,
                    SchemaName: ProviderSchemaName,
                    Temperature: 0.1),
                provider,
                JsonOpts,
                ct).ConfigureAwait(false);
            return completion.Value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception cause)
        {
            logger.LogWarning(cause, "Partner extraction failed for {Url}; page skipped.", page.Url);
            return null;
        }
    }

    private static string BuildUserPrompt(GccQuoteablePage page, IReadOnlyList<string>? partnerToolNames)
    {
        var body = new StringBuilder();
        body.Append("URL: ").AppendLine(page.Url);
        body.Append("Title: ").AppendLine(page.Title);
        if (!string.IsNullOrWhiteSpace(page.SectionTitle))
            body.Append("Section: ").AppendLine(page.SectionTitle);

        if (partnerToolNames is { Count: > 0 })
        {
            body.Append("Partner product names in scope: ")
                .AppendLine(string.Join(", ", partnerToolNames.Take(12)));
        }

        if (page.Headings.Count > 0)
        {
            body.AppendLine().AppendLine("Headings:");
            foreach (var h in page.Headings.Take(GccResearchCaps.MaxHeadingsPerPage))
                body.Append("  H").Append(h.Level).Append(": ").AppendLine(h.Text);
        }

        var paragraphs = page.Paragraphs
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Take(MaxParagraphsPerPage)
            .ToList();
        if (paragraphs.Count == 0 && page.Headings.Count == 0) return "";

        body.AppendLine().AppendLine("Page text:");
        foreach (var p in paragraphs)
            body.AppendLine(p.Length > MaxParagraphChars ? p[..MaxParagraphChars] : p);

        return body.ToString();
    }

    private static IReadOnlyList<T> Dedupe<T>(List<T> items, Func<T, string> key) =>
        items.GroupBy(i => key(i).Trim().ToLowerInvariant())
            .Where(g => g.Key.Length > 0)
            .Select(g => g.First())
            .ToList();
}

/* ------------------------------------------------------------------ *
 * Schema-constrained response shape. Every item carries the verbatim  *
 * source quote so verify can check it against the source page text.  *
 * ------------------------------------------------------------------ */

internal sealed record PartnerPageExtraction(
    List<PartnerCitableItem>? Citables,
    List<PartnerAdItem>? Advertisements,
    List<PartnerComparisonItem>? Comparisons,
    List<PartnerAlternativeItem>? Alternatives,
    List<PartnerPricingItem>? Pricing,
    List<PartnerIcpItem>? Icp,
    List<PartnerIntegrationItem>? Integrations,
    List<PartnerFaqItem>? Faqs,
    List<PartnerCaseStudyItem>? CaseStudies,
    List<PartnerTestimonialItem>? Testimonials,
    List<PartnerAwardItem>? Awards,
    List<PartnerFeatureItem>? FeatureInventory,
    List<PartnerConstraintItem>? TechnicalConstraints,
    List<PartnerOfferCtaItem>? OfferCtas,
    List<PartnerDisqualifierItem>? Disqualifiers,
    List<PartnerPlaybookItem>? UseCasePlaybooks,
    List<PartnerCategoryItem>? Categories,
    List<PartnerFreshnessItem>? Freshness,
    List<PartnerBattlecardItem>? BattlecardSlices,
    List<PartnerDemoBeatItem>? DemoBeats,
    List<PartnerComplianceItem>? ComplianceSnippets,
    List<PartnerDisclosureItem>? AffiliateDisclosures);

internal sealed record PartnerCitableItem(string IsolatedClaim, string? Quote);
internal sealed record PartnerAdItem(string MarketingHook, string? PainPointTrigger, string? CtaWrapper, string? Quote);
internal sealed record PartnerComparisonItem(string StandardizedFeatureId, string CapabilityPayload, string? NormalizedCost, string? Quote);
internal sealed record PartnerAlternativeItem(string TriggerDeficit, List<string>? RecommendedSwap, string? PivotCopy, string? Quote);
internal sealed record PartnerPricingItem(string TierName, decimal? ListPrice, string? PriceCurrency, string? BillingPeriod, string? FeatureGates, string? FreeOrTrial, string? OverageTerms, decimal? UnitCostAmount, string? UnitCostBasis, string? Quote);
internal sealed record PartnerIcpItem(List<string>? ServedSegments, List<string>? ExcludedSegments, string? CompanySizeBand, List<string>? Industries, List<string>? BuyerRoles, string? Quote);
internal sealed record PartnerIntegrationItem(string IntegrationName, string? IntegrationType, string? ApiOrSdk, string? Quote);
internal sealed record PartnerFaqItem(string Question, string Answer, string? Quote);
internal sealed record PartnerCaseStudyItem(string ClientName, string? Sector, string OutcomeClaim, string? MetricName, string? MetricValue, string? Quote);
internal sealed record PartnerTestimonialItem(string QuoteText, string? AttributedTo, string? BuyerRole, string? Industry, string? Quote);
internal sealed record PartnerAwardItem(string AwardName, string? Source, string? AwardedFor, string? AwardedPeriod, string? Quote);
internal sealed record PartnerFeatureItem(string FeatureName, string? FeatureCategory, string? GatedToTier, string? Quote);
internal sealed record PartnerConstraintItem(string? ConstraintKind, decimal? LimitValue, string? LimitUnit, string LimitText, string? Quote);
internal sealed record PartnerOfferCtaItem(string CtaLabel, string DestinationUrl, string? OfferType, string? Quote);
internal sealed record PartnerDisqualifierItem(string? LimitType, string LimitDetail, string? Quote);
internal sealed record PartnerPlaybookItem(string JobToBeDone, List<string>? CitedStepsOrFeatures, string? Quote);
internal sealed record PartnerCategoryItem(string PrimaryCategory, string? VsCategoryLabel, string? Quote);
internal sealed record PartnerFreshnessItem(string? ChangeKind, string ChangeSummary, string? StatedAsOf, string? Quote);
internal sealed record PartnerBattlecardItem(string WinTheme, string? Landmine, string? CoachingLine, string? Quote);
internal sealed record PartnerDemoBeatItem(string BeatTitle, string BeatClaim, string? Quote);
internal sealed record PartnerComplianceItem(string? TermKind, string TermText, string? Quote);
internal sealed record PartnerDisclosureItem(string DisclosureText, string? JurisdictionOrPolicy, string? Quote);
