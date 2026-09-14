namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// Structured competitor library payloads (competitor-extraction plan §5–§7).
/// Always <c>crawlType:"competitor"</c> (or <c>competitors</c>). Never label as partner.
/// </summary>
public sealed record GccCompetitorExtractionDocument(
    string ExtractorVersion,
    DateTimeOffset ExtractedAtUtc,
    IReadOnlyList<GccCompetitorPricingTierAsset> PricingCatalog,
    IReadOnlyList<GccCompetitorIcpAsset> Icp,
    IReadOnlyList<GccCompetitorIntegrationAsset> Integrations,
    IReadOnlyList<GccCompetitorFaqAsset> FaqBank,
    IReadOnlyList<GccCompetitorProofAsset> ProofPack,
    IReadOnlyList<GccCompetitorOfferCtaAsset> OfferCtas,
    IReadOnlyList<GccCompetitorDisqualifierAsset> Disqualifiers,
    IReadOnlyList<GccCompetitorGapMapAsset> GapMap,
    IReadOnlyList<GccCompetitorFramingAsset> FramingBank,
    IReadOnlyList<GccCompetitorDemandSignalAsset> DemandSignals,
    IReadOnlyList<GccCompetitorTypeLabelAsset> TypeLabels,
    IReadOnlyList<GccCompetitorDeficitRouterAsset> DeficitRouter,
    IReadOnlyList<GccCompetitorComparisonAxisAsset> ComparisonAxes,
    IReadOnlyList<GccCompetitorClaimRiskAsset> ClaimRiskFlags,
    IReadOnlyList<GccCompetitorAdThemeAsset> AdThemes,
    IReadOnlyList<GccCompetitorOutlineCloneAsset> OutlineClones,
    IReadOnlyList<GccCompetitorSerpPostureAsset> SerpPosture,
    IReadOnlyList<GccCompetitorChangelogAsset> Changelog,
    IReadOnlyList<GccCompetitorComplianceSnippetAsset> ComplianceSnippets,
    Dictionary<string, object?>? SoftwareApplicationJsonLd = null)
{
    public const string CurrentExtractorVersion = "gcc-competitor-extraction.v1";
    public const string CrawlTypeCompetitor = "competitors";
}

public sealed record GccCompetitorPricingTierAsset(
    string TierName,
    decimal? ListPrice,
    string? PriceCurrency,
    string? BillingPeriod,
    string? FeatureGates,
    string? FreeOrTrial,
    string? OverageTerms,
    string? PriceEffectiveDate,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorIcpAsset(
    IReadOnlyList<string> ServedSegments,
    IReadOnlyList<string> ExcludedSegments,
    string? CompanySizeBand,
    IReadOnlyList<string> Industries,
    IReadOnlyList<string> BuyerRoles,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorIntegrationAsset(
    string IntegrationName,
    string? IntegrationType,
    string? ApiOrSdk,
    string? MarketplacePresence,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorFaqAsset(
    string Question,
    string VerifiedAnswer,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorProofAsset(
    string ProofKind,
    string ProofClaim,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorOfferCtaAsset(
    string CtaLabel,
    string DestinationUrl,
    string OfferType,
    string? CtaWrapper,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorDisqualifierAsset(
    string LimitType,
    string LimitDetail,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorGapMapAsset(
    string GapTopic,
    string DepthAssessment,
    string? BriefIntentLink,
    string? RivalUrl,
    string OpportunityForUs,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorFramingAsset(
    string FramedRivalName,
    string FrameType,
    string FrameExcerpt,
    string Sentiment,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorDemandSignalAsset(
    string? PrimaryKeywordFocus,
    string ContentFormat,
    IReadOnlyList<string> ContentHierarchy,
    string? SearchIntentCategory,
    string? AdOrCopyTheme,
    string? StructureWorthBeating,
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
    string? RivalNormalizedCost,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorClaimRiskAsset(
    string ClaimText,
    string RiskKind,
    string? StatedAsOf,
    string OriginProofUrl,
    string WriteGuidance,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorAdThemeAsset(
    string HeadlinePattern,
    string OfferPromise,
    string? LandingPromise,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorOutlineCloneAsset(
    IReadOnlyList<string> OutlineSkeleton,
    string SourceUrl,
    string? IntentCategory,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorSerpPostureAsset(
    string CoverageTopic,
    string? AuthoritySignal,
    string OpportunityForUs,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorChangelogAsset(
    string ChangeKind,
    string ChangeSummary,
    string? StatedAsOf,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccCompetitorComplianceSnippetAsset(
    string TermKind,
    string TermText,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);
