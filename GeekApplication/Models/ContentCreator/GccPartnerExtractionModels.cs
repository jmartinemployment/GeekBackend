namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// Structured partner library payloads (partner-extraction plan §2–§8).
/// Always <c>crawlType:"partner"</c>. Excerpt-only <see cref="GccQuoteablePage"/> does not satisfy these assets.
/// Pruned shape: fields with zero real downstream consumers (verified by repo-wide grep during the
/// Workstream 1 synthesis rebuild) were removed rather than carried forward. See the Workstream 1
/// final report for the exact drop list and rationale.
/// </summary>
public sealed record GccPartnerExtractionDocument(
    string ExtractorVersion,
    IReadOnlyList<GccPartnerCitableAsset> Citables,
    IReadOnlyList<GccPartnerAdvertisementAsset> Advertisements,
    IReadOnlyList<GccPartnerComparisonAsset> Comparisons,
    IReadOnlyList<GccPartnerAlternativesAsset> Alternatives,
    IReadOnlyList<GccPartnerPricingTierAsset> PricingCatalog,
    IReadOnlyList<GccPartnerIcpAsset> Icp,
    IReadOnlyList<GccPartnerIntegrationAsset> Integrations,
    IReadOnlyList<GccPartnerFaqAsset> FaqBank,
    IReadOnlyList<GccPartnerProofAsset> ProofPack,
    IReadOnlyList<GccPartnerOfferCtaAsset> OfferCtas,
    IReadOnlyList<GccPartnerDisqualifierAsset> Disqualifiers,
    IReadOnlyList<GccPartnerUseCasePlaybookAsset> UseCasePlaybooks,
    IReadOnlyList<GccPartnerCategoryAsset> Categories,
    IReadOnlyList<GccPartnerFreshnessAsset> FreshnessLog,
    IReadOnlyList<GccPartnerBattlecardSliceAsset> BattlecardSlices,
    IReadOnlyList<GccPartnerDemoBeatAsset> DemoBeats,
    IReadOnlyList<GccPartnerComplianceSnippetAsset> ComplianceSnippets,
    IReadOnlyList<GccPartnerAffiliateDisclosureAsset> AffiliateDisclosures)
{
    public const string CurrentExtractorVersion = "gcc-partner-extraction.v3";
    public const string CrawlTypePartner = "partner";
    public const string CrawlTypeCompetitors = "competitors";
}

/// <summary>Shared provenance for every partner extraction asset (Appendix C fields where applicable).</summary>
public sealed record GccPartnerExtractionProvenance(
    string OriginProofUrl,
    string CrawlType,
    string? RunId,
    string? PageId,
    string? SectionTitle,
    string? SourceDigest,
    DateTimeOffset? TemporalAnchorUtc,
    /// <summary>Exact quote span used for verify (usually the claim / answer text).</summary>
    string? Quote = null,
    /// <summary>0-based start offset of <see cref="Quote"/> in source Markdown when verified. Required by master-plan Appendix C ("quote/offsets"); restored 2026-09-15 — the prior pruning pass removed it despite ~18 live consumers including GccV2CitationEvidenceGuard, GccV2ValidateService, RagGenerateService.</summary>
    int? StartChar = null,
    /// <summary>0-based end offset (exclusive) of <see cref="Quote"/> in source Markdown when verified.</summary>
    int? EndChar = null,
    /// <summary>Create section key this asset supports when attached as a citation.</summary>
    string? SectionKey = null,
    /// <summary>consented | licensed | unknown | prohibited. Missing ≡ unknown until VALIDATE stamps.</summary>
    string? SourceRights = null,
    /// <summary>True only after quote↔Markdown verify against GET /v1/pages.</summary>
    bool MarkdownVerified = false);

public sealed record GccPartnerCitableAsset(
    string IsolatedClaim,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerAdvertisementAsset(
    string MarketingHook,
    string PainPointTrigger,
    string CtaWrapper,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerComparisonAsset(
    string StandardizedFeatureId,
    string CapabilityPayload,
    string? NormalizedCost,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerAlternativesAsset(
    string TriggerDeficit,
    IReadOnlyList<string> RecommendedSwap,
    string PivotCopy,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerPricingTierAsset(
    string TierName,
    decimal? ListPrice,
    string? PriceCurrency,
    string? BillingPeriod,
    /// <summary>Restored 2026-09-15 — live consumer: GccV2PartnerExtractionVerify uses this as verify-quote fallback text when present.</summary>
    string? FeatureGates,
    /// <summary>Restored 2026-09-15 — live consumer: GccV2PartnerExtractionVerify uses this as verify-quote fallback text when present.</summary>
    string? FreeOrTrial,
    string? OverageTerms,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerIcpAsset(
    IReadOnlyList<string> ServedSegments,
    IReadOnlyList<string> ExcludedSegments,
    string? CompanySizeBand,
    IReadOnlyList<string> Industries,
    IReadOnlyList<string> BuyerRoles,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerIntegrationAsset(
    string IntegrationName,
    string? IntegrationType,
    string? ApiOrSdk,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerFaqAsset(
    string Question,
    string VerifiedAnswer,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerProofAsset(
    string ProofKind,
    string ProofClaim,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerOfferCtaAsset(
    string CtaLabel,
    string DestinationUrl,
    string OfferType,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerDisqualifierAsset(
    string LimitType,
    string LimitDetail,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerUseCasePlaybookAsset(
    string JobToBeDone,
    IReadOnlyList<string> CitedStepsOrFeatures,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerCategoryAsset(
    string PrimaryCategory,
    string? VsCategoryLabel,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerFreshnessAsset(
    string ChangeKind,
    string ChangeSummary,
    string? StatedAsOf,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerBattlecardSliceAsset(
    string WinTheme,
    string Landmine,
    string CoachingLine,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerDemoBeatAsset(
    string BeatTitle,
    string BeatClaim,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerComplianceSnippetAsset(
    string TermKind,
    string TermText,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

/// <summary>
/// Affiliate/reseller disclosure — <see cref="JurisdictionOrPolicy"/> is a real business requirement
/// for this operator's affiliate model (FTC/consumer-protection disclosure regimes vary by
/// jurisdiction), so it is kept even though nothing reads it yet; PLAN/WRITE wiring for it is
/// tracked as follow-up, not dropped as dead weight.
/// </summary>
public sealed record GccPartnerAffiliateDisclosureAsset(
    string DisclosureText,
    string? JurisdictionOrPolicy,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);
