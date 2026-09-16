namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// Structured competitor library payloads. Always <c>crawlType:"competitors"</c>; never label as partner.
///
/// A competitor is whoever competes for the operator's clients or search traffic. It is never a product:
/// software is a <b>partner</b> to recommend and implement, never a rival. Two kinds:
///   <c>ProfessionalService</c> - a rival consultancy competing for the same engagements.
///   content competitor        - a publisher, review site or directory competing for the same search
///                               attention; media, not a service business.
/// This shape is therefore agency-native and deliberately shares no record type with
/// <see cref="GccPartnerExtractionDocument"/>, including provenance: a competitor must never be able to
/// hydrate into a partner shape.
///
/// Field groups map 1:1 to the four jobs competitor data does (plans/rag-foundation-rewrite.md §1):
/// content gaps, bottom-of-funnel comparison, trust through honesty, and SEO de-risking.
/// </summary>
public sealed record GccCompetitorExtractionDocument(
    string ExtractorVersion,

    // SEPARATE CONCERN — content gap analysis is outside the scope of Content Creation.
    // These two fields serve gap discovery ("what should we write about?"), which belongs to a
    // different system. They are retained here only because Information Gain consumes them during
    // drafting to avoid restating what this site already covers — an anti-duplication check, not
    // topic discovery. Do not grow gap-analysis capability on this document; if Information Gain
    // stops needing them, both fields leave with it.
    IReadOnlyList<GccCompetitorCoverageAsset> CoverageMap,
    IReadOnlyList<GccCompetitorGapMapAsset> GapMap,

    // Job 2 — capture bottom-of-funnel intent
    IReadOnlyList<GccCompetitorComparisonAxisAsset> ComparisonAxes,
    IReadOnlyList<GccCompetitorDeficitRouterAsset> DeficitRouter,

    // Job 3 — build trust through honesty
    IReadOnlyList<GccCompetitorFaqAsset> FaqBank,
    IReadOnlyList<GccCompetitorProofAsset> ProofPack,
    IReadOnlyList<GccCompetitorFramingAsset> FramingBank,
    IReadOnlyList<GccCompetitorClaimRiskAsset> ClaimRiskFlags,

    // Job 4 — de-risk SEO strategy
    IReadOnlyList<GccCompetitorDemandSignalAsset> DemandSignals,

    // Routing — direct/both vs content-only (GccV2CompetitorTypePlanRouting)
    IReadOnlyList<GccCompetitorTypeLabelAsset> TypeLabels,

    // Agency-native profile — the signals a rival service business actually exposes
    IReadOnlyList<GccCompetitorServiceAsset> ServiceOfferings,
    IReadOnlyList<GccCompetitorClientProofAsset> NamedClients,
    IReadOnlyList<GccCompetitorPresenceAsset> GeographicPresence,
    IReadOnlyList<GccCompetitorCredentialAsset> TeamCredentials,
    IReadOnlyList<GccCompetitorPositioningAsset> PositioningStatements,
    IReadOnlyList<GccCompetitorBoundaryAsset> Disqualifiers,
    IReadOnlyList<GccCompetitorMediaProfileAsset> MediaProfile)
{
    public const string CurrentExtractorVersion = "gcc-competitor-extraction.v4";
    public const string CrawlTypeCompetitor = "competitors";
}

/// <summary>
/// Competitor-owned provenance. Deliberately NOT <see cref="GccPartnerExtractionProvenance"/> — sharing
/// that type is what previously let competitor assets hydrate into partner shapes.
/// Appendix C fields (quote + offsets) are required for citation verify.
/// </summary>
public sealed record GccCompetitorExtractionProvenance(
    string OriginProofUrl,
    string CrawlType,
    string? RunId,
    string? PageId,
    string? SectionTitle,
    string? SourceDigest,
    DateTimeOffset? TemporalAnchorUtc,
    /// <summary>Exact quote span used for verify.</summary>
    string? Quote = null,
    /// <summary>0-based start offset of <see cref="Quote"/> in source Markdown when verified.</summary>
    int? StartChar = null,
    /// <summary>0-based end offset (exclusive) of <see cref="Quote"/> in source Markdown when verified.</summary>
    int? EndChar = null,
    /// <summary>Create section key this asset supports when attached as a citation.</summary>
    string? SectionKey = null,
    /// <summary>consented | licensed | unknown | prohibited. Missing means unknown until VALIDATE stamps.</summary>
    string? SourceRights = null,
    /// <summary>True only after quote-to-Markdown verify against GET /v1/pages.</summary>
    bool MarkdownVerified = false);

/* ---------------------------------------------------------------- *
 * Job 1 — content gaps                                             *
 * ---------------------------------------------------------------- */

/// <summary>
/// What the rival covers and how deeply.
///
/// SEPARATE CONCERN: this is content gap analysis, which is outside Content Creation's scope. Its only
/// in-scope consumer is Information Gain, which uses it to stop a draft restating coverage this site
/// already has. It is not a topic-discovery feature and must not become one here.
/// </summary>
public sealed record GccCompetitorCoverageAsset(
    string TopicPath,
    string DepthAssessment,
    IReadOnlyList<string> EvidenceHeadings,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

/// <summary>
/// A topic the rival covers that this site does not, framed as an opportunity.
///
/// SEPARATE CONCERN: content gap analysis belongs to a different system. Retained only as the source
/// of Information Gain's <c>CompetitorOpens</c> during drafting (GccV2InformationGain.Enrich).
/// </summary>
public sealed record GccCompetitorGapMapAsset(
    string GapTopic,
    string DepthAssessment,
    string OpportunityForUs,
    GccCompetitorExtractionProvenance Provenance);

/* ---------------------------------------------------------------- *
 * Job 2 — bottom-of-funnel comparison                              *
 * ---------------------------------------------------------------- */

/// <summary>
/// A comparison axis that works for a service business. <see cref="AxisId"/> is the shared join key
/// against partner strengths — see <see cref="GccCompetitorDeficitRouterAsset"/>.
/// </summary>
public sealed record GccCompetitorComparisonAxisAsset(
    string AxisId,
    string AxisLabel,
    string RivalCapabilityPayload,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

/// <summary>
/// One half of the counterweight: a rival's service gap, bound to the partner strength that answers it.
/// Admissible only when both chunk ids are present and both sides sit on the same <see cref="AxisId"/>.
/// </summary>
public sealed record GccCompetitorDeficitRouterAsset(
    string AxisId,
    string TriggerDeficit,
    string OriginProofUrl,
    IReadOnlyList<string> RecommendedSwap,
    string? PivotCopy,
    string? CompetitorDeficitChunkId,
    string? PartnerStrengthChunkId,
    GccCompetitorExtractionProvenance Provenance);

/* ---------------------------------------------------------------- *
 * Job 3 — trust through honesty                                    *
 * ---------------------------------------------------------------- */

public sealed record GccCompetitorFaqAsset(
    string Question,
    string VerifiedAnswer,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

public sealed record GccCompetitorProofAsset(
    string ProofKind,
    string ProofClaim,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

public sealed record GccCompetitorFramingAsset(
    string FrameType,
    string FrameExcerpt,
    string Sentiment,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

/// <summary>Rival marketing that must never be echoed as verified fact (fail-closed VALIDATE gate).</summary>
public sealed record GccCompetitorClaimRiskAsset(
    string ClaimText,
    string RiskKind,
    string OriginProofUrl,
    string WriteGuidance,
    GccCompetitorExtractionProvenance Provenance);

/* ---------------------------------------------------------------- *
 * Job 4 — SEO de-risking                                           *
 * ---------------------------------------------------------------- */

public sealed record GccCompetitorDemandSignalAsset(
    string? PrimaryKeywordFocus,
    string ContentFormat,
    string? SearchIntentCategory,
    string? AdOrCopyTheme,
    GccCompetitorExtractionProvenance Provenance);

/* ---------------------------------------------------------------- *
 * Routing                                                          *
 * ---------------------------------------------------------------- */

/// <summary>
/// <c>direct</c> | <c>both</c> | <c>content</c>. Content-only rivals inform gap analysis and SEO only —
/// they must never be offered as product substitutes (GccV2CompetitorTypePlanRouting).
/// </summary>
public sealed record GccCompetitorTypeLabelAsset(
    string CompetitorType,
    string TypeRationale,
    string EntityName,
    string PrimaryUrl,
    GccCompetitorExtractionProvenance Provenance);

/* ---------------------------------------------------------------- *
 * Agency-native profile                                            *
 * ---------------------------------------------------------------- */

public sealed record GccCompetitorServiceAsset(
    string ServiceName,
    string? EngagementModel,
    string? Specialism,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

public sealed record GccCompetitorClientProofAsset(
    string ClientName,
    string? Sector,
    string? OutcomeClaim,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

public sealed record GccCompetitorPresenceAsset(
    string Location,
    string PresenceKind,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

public sealed record GccCompetitorCredentialAsset(
    string CredentialName,
    string CredentialKind,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

public sealed record GccCompetitorPositioningAsset(
    string PositioningStatement,
    string? AudienceFocus,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

/// <summary>
/// A boundary the rival <b>states</b> about who it does not serve — sector, company size, or region.
/// Qualifying intelligence, and the only admissible source of a deficit: an absence of mention is not
/// a boundary (plans/rag-foundation-rewrite.md §1).
/// </summary>
public sealed record GccCompetitorBoundaryAsset(
    string BoundaryKind,
    string BoundaryDetail,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);

/// <summary>
/// Signals for a <b>content competitor</b> — a publisher, review site or directory. These entities have
/// no service offerings, clients or credentials, so forcing them through the consultancy fields yields
/// nothing. They compete for search attention, which is what this records.
/// </summary>
public sealed record GccCompetitorMediaProfileAsset(
    string? PublicationCadence,
    IReadOnlyList<string> FormatMix,
    string? MonetisationModel,
    string? TopicalAuthorityNote,
    string OriginProofUrl,
    GccCompetitorExtractionProvenance Provenance);
