namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// Dominant SERP formats inferred from organic titles (advisory for Angle for SEO — not auto-set).
/// </summary>
public sealed record SerpShapeSummary(
    IReadOnlyList<string> DominantFormats,
    IReadOnlyList<string> TitlePatterns,
    string Guidance,
    bool HasPeopleAlsoAsk,
    int OrganicCount,
    string? PageHint);

/// <summary>
/// People Also Ask questions plus related searches, for outline/FAQ curation.
/// </summary>
/// <remarks>
/// Named <c>PaaPafCluster</c> until 2026-09-21 -- renamed to resolve a collision: PAF means
/// "People Also Found" in this codebase's older usage but "Primary Answer Feature" in Geek-SEO
/// (SiteAnalyzerGates.cs). This record never actually held PAF-sourced data (no field for it,
/// zero call sites), so the fix is dropping the term rather than finding a third meaning for it.
/// </remarks>
public sealed record PaaCluster(
    IReadOnlyList<PaaCandidate> Questions,
    IReadOnlyList<string> RelatedSearches);

public sealed record PaaCandidate(
    string Question,
    bool LikelyRelevant,
    string? Reason);

/// <summary>
/// Information Gain note: this-site coverage (from crawl) plus optional SERP competitor gaps.
/// </summary>
public sealed record InformationGainNote(
    IReadOnlyList<string> ThisSiteCovers,
    IReadOnlyList<string> CompetitorOpens,
    string Summary);

/// <summary>Parsed operator-supplied Google results page (file ingest).</summary>
public sealed record SavedSerpParseResult(
    IReadOnlyList<SavedSerpOrganic> Organics,
    IReadOnlyList<PaaCandidate> PeopleAlsoAsk,
    IReadOnlyList<string> RelatedSearches,
    SerpShapeSummary Shape,
    bool MissingPaaLikelyPage2,
    string? ParseWarning);

public sealed record SavedSerpOrganic(
    string Title,
    string Url,
    int Position);
