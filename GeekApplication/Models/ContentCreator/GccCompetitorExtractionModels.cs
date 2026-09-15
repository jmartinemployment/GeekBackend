namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// Structured competitor library payloads (competitor-extraction plan §5–§7).
/// Always <c>crawlType:"competitor"</c> (or <c>competitors</c>). Never label as partner.
/// Pruned shape: Icp, Integrations, ComplianceSnippets, AdThemes, OutlineClones, SerpPosture,
/// Changelog, and the dead SoftwareApplicationJsonLd cache field were removed — confirmed zero
/// real downstream consumers by direct grep 2026-09-15. OfferCtas was also removed: a competitor
/// (a rival business) must never carry a sell CTA/destination URL (see
/// plans/rag-foundation-rewrite.md §0.000, plans/competitor-extraction-complete.md §1).
/// CORRECTION 2026-09-15: FaqBank/ProofPack were removed by an earlier pass whose "zero real
/// downstream consumers... verified by repo-wide grep" claim was false — both are live consumers
/// of GccV2CompetitorExtractionVerify.cs. Restored. "See the Workstream 1 final report" — no such
/// report exists on disk; do not cite it as a source.
/// </summary>
public sealed record GccCompetitorExtractionDocument(
    string ExtractorVersion,
    IReadOnlyList<GccCompetitorPricingTierAsset> PricingCatalog,
    IReadOnlyList<GccCompetitorFaqAsset> FaqBank,
    IReadOnlyList<GccCompetitorProofAsset> ProofPack,
    IReadOnlyList<GccCompetitorDisqualifierAsset> Disqualifiers,
    IReadOnlyList<GccCompetitorGapMapAsset> GapMap,
    IReadOnlyList<GccCompetitorFramingAsset> FramingBank,
    IReadOnlyList<GccCompetitorDemandSignalAsset> DemandSignals,
    IReadOnlyList<GccCompetitorTypeLabelAsset> TypeLabels,
    IReadOnlyList<GccCompetitorDeficitRouterAsset> DeficitRouter,
    IReadOnlyList<GccCompetitorComparisonAxisAsset> ComparisonAxes,
    IReadOnlyList<GccCompetitorClaimRiskAsset> ClaimRiskFlags)
{
    public const string CurrentExtractorVersion = "gcc-competitor-extraction.v3";
    public const string CrawlTypeCompetitor = "competitors";
}

public sealed record GccCompetitorPricingTierAsset(
    string TierName,
    decimal? ListPrice,
    string? PriceCurrency,
    string? BillingPeriod,
    string? OverageTerms,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

/// <summary>Restored 2026-09-15 — real consumer in GccV2CompetitorExtractionVerify.cs.</summary>
public sealed record GccCompetitorFaqAsset(
    string Question,
    string VerifiedAnswer,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

/// <summary>Restored 2026-09-15 — real consumer in GccV2CompetitorExtractionVerify.cs.</summary>
public sealed record GccCompetitorProofAsset(
    string ProofKind,
    string ProofClaim,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorDisqualifierAsset(
    string LimitType,
    string LimitDetail,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorGapMapAsset(
    string GapTopic,
    string DepthAssessment,
    string OpportunityForUs,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorFramingAsset(
    string FrameType,
    string FrameExcerpt,
    string Sentiment,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorDemandSignalAsset(
    string? PrimaryKeywordFocus,
    string ContentFormat,
    string? SearchIntentCategory,
    string? AdOrCopyTheme,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorTypeLabelAsset(
    string CompetitorType,
    string TypeRationale,
    string EntityName,
    string PrimaryUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorDeficitRouterAsset(
    string TriggerDeficit,
    string OriginProofUrl,
    IReadOnlyList<string> RecommendedSwap,
    string? PivotCopy,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorComparisonAxisAsset(
    string StandardizedFeatureId,
    string RivalCapabilityPayload,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorClaimRiskAsset(
    string ClaimText,
    string RiskKind,
    string OriginProofUrl,
    string WriteGuidance,
    GccPartnerExtractionProvenance Provenance);
