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
    IReadOnlyList<GccPartnerCaseStudyAsset> CaseStudies,
    IReadOnlyList<GccPartnerTestimonialAsset> Testimonials,
    IReadOnlyList<GccPartnerAwardAsset> Awards,
    IReadOnlyList<GccPartnerFeatureAsset> FeatureInventory,
    IReadOnlyList<GccPartnerTechnicalConstraintAsset> TechnicalConstraints,
    IReadOnlyList<GccPartnerOfferCtaAsset> OfferCtas,
    IReadOnlyList<GccPartnerDisqualifierAsset> Disqualifiers,
    IReadOnlyList<GccPartnerUseCasePlaybookAsset> UseCasePlaybooks,
    IReadOnlyList<GccPartnerCategoryAsset> Categories,
    IReadOnlyList<GccPartnerFreshnessAsset> FreshnessLog,
    IReadOnlyList<GccPartnerBattlecardSliceAsset> BattlecardSlices,
    IReadOnlyList<GccPartnerDemoBeatAsset> DemoBeats,
    IReadOnlyList<GccPartnerComplianceSnippetAsset> ComplianceSnippets,
    IReadOnlyList<GccPartnerAffiliateDisclosureAsset> AffiliateDisclosures,
    /// <summary>
    /// How many pages extraction was handed, and how many of those failed outright (the provider
    /// call threw and the page was skipped).
    ///
    /// AGENTS.md, "Fail closed. No middle states.": <i>"Partial extraction is failure. Catching a
    /// per-page error and logging 'page skipped' is a middle state; it hid a total extraction
    /// outage behind thirteen drafts of filler."</i> Without these counts a total outage and a
    /// genuine data shortage produce byte-identical documents -- both simply empty -- so the
    /// refusal downstream could only ever say "nothing found" for either cause.
    /// </summary>
    int PagesAttempted = 0,
    int PagesFailed = 0,
    /// <summary>The first provider error, verbatim, when PagesFailed > 0. Counts say a
    /// fault happened; only the provider's own message says what the fault was.</summary>
    string? FirstFailure = null)
{
    // Kept in lockstep with GccV2PartnerExtractionService.ProviderSchemaName ("partner-extraction-v4")
    // -- was "v3" while the provider schema had already moved to v4, an unnoticed drift caught during
    // a 2026-09-22 audit against the restored partner-extraction-complete.md spec.
    public const string CurrentExtractorVersion = "gcc-partner-extraction.v4";
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
    /// <summary>0-based start offset of <see cref="Quote"/> in the source page text when verified. Required by master-plan Appendix C ("quote/offsets"); restored 2026-09-15 — the prior pruning pass removed it despite ~18 live consumers including GccV2CitationEvidenceGuard, GccV2ValidateService, GccV2CreateLibraryWriter.</summary>
    int? StartChar = null,
    /// <summary>0-based end offset (exclusive) of <see cref="Quote"/> in the source page text when verified.</summary>
    int? EndChar = null,
    /// <summary>Create section key this asset supports when attached as a citation.</summary>
    string? SectionKey = null,
    /// <summary>consented | licensed | unknown | prohibited. Missing ≡ unknown until VALIDATE stamps.</summary>
    string? SourceRights = null,
    /// <summary>True only after quote↔page-text verify against GET /v1/pages.</summary>
    bool QuoteVerified = false);

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
    /// <summary>Structured per-unit cost, e.g. 15 / "seat" or 0.02 / "credit". Null when not published.</summary>
    decimal? UnitCostAmount,
    /// <summary>The unit <see cref="UnitCostAmount"/> is charged per: seat, credit, user, request.</summary>
    string? UnitCostBasis,
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

/// <summary>
/// Proof was previously one undifferentiated bucket (ProofKind + ProofClaim free text), so a case
/// study, a testimonial and a G2 badge were indistinguishable and could not be injected at the moment
/// each is persuasive. They are now three addressable types.
/// </summary>
public sealed record GccPartnerCaseStudyAsset(
    string ClientName,
    string? Sector,
    string OutcomeClaim,
    /// <summary>The headline metric, e.g. "ROI" or "time saved". Null when the study states none.</summary>
    string? MetricName,
    /// <summary>The metric's stated value, e.g. "40%" or "12 hours/week". Never computed or rounded.</summary>
    string? MetricValue,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

public sealed record GccPartnerTestimonialAsset(
    string QuoteText,
    string? AttributedTo,
    string? BuyerRole,
    string? Industry,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

/// <param name="Source">Normalized: g2 | capterra | producthunt | trustpilot | other.</param>
public sealed record GccPartnerAwardAsset(
    string AwardName,
    string Source,
    string? AwardedFor,
    string? AwardedPeriod,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

/// <summary>
/// The definitive list of what the tool does, distinct from <see cref="GccPartnerComparisonAsset"/>
/// which is a comparison vector. Prevents the model asserting capabilities the product lacks.
/// </summary>
public sealed record GccPartnerFeatureAsset(
    string FeatureName,
    string? FeatureCategory,
    /// <summary>Tier this feature is gated behind when the page states one.</summary>
    string? GatedToTier,
    string OriginProofUrl,
    GccPartnerExtractionProvenance Provenance);

/// <param name="ConstraintKind">api_rate_limit | storage_cap | character_limit | seat_cap | other.</param>
/// <param name="LimitValue">Numeric limit when published; null when stated only qualitatively.</param>
public sealed record GccPartnerTechnicalConstraintAsset(
    string ConstraintKind,
    decimal? LimitValue,
    string? LimitUnit,
    string LimitText,
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
