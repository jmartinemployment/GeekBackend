namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// Persisted research on a Content Creator create. Quoteables and SerpPages are unlimited
/// (per-page heading/paragraph/organic trimming below bounds prompt size, not a total-file cap).
/// </summary>
public sealed record GccResearchDocument(
    GccSerpIndex? SerpIndex,
    IReadOnlyList<GccQuoteablePage> Quoteables,
    IReadOnlyList<GccKeywordSource>? Sources = null,
    IReadOnlyList<GccParsedSerpPage>? SerpPages = null);

/// <summary>
/// A parsed Keyword (Google SERP) upload — organics + related searches only. PAA is intentionally
/// never captured here (discarded at parse time); it stays a manually hand-entered brief field.
/// Shape.Guidance is advisory and surfaces in the UI only (operator adds it to brief notes
/// themselves via "Add to notes") — it is never auto-injected into the Generate prompt.
/// </summary>
public sealed record GccParsedSerpPage(
    string Id,
    string FileName,
    IReadOnlyList<SavedSerpOrganic> Organics,
    IReadOnlyList<string> RelatedSearches,
    SerpShapeSummary Shape,
    string? ParseWarning);

/// <summary>API response for a keyword source: metadata plus its parsed SERP page, if any (KeywordResult only).</summary>
public sealed record GccKeywordSourceDetail(
    string Id,
    string FileName,
    string Category,
    int HeadingCount,
    int ParagraphCount,
    int QuestionCount,
    GccParsedSerpPage? SerpPage);

/// <summary>
/// An operator-uploaded research file attached to a create (CWv2-style keyword source).
/// Uploading is the research action — no follow/process step. Unlimited per create.
/// </summary>
public sealed record GccKeywordSource(
    string Id,
    string FileName,
    string Category,
    int HeadingCount,
    int ParagraphCount,
    int QuestionCount);

public sealed record GccSerpIndex(
    IReadOnlyList<string> OrganicTitles,
    IReadOnlyList<string> OrganicUrls,
    IReadOnlyList<string> PeopleAlsoAsk,
    IReadOnlyList<string> RelatedSearches);

public sealed record GccQuoteablePage(
    string Url,
    string Title,
    IReadOnlyList<string> Headings,
    IReadOnlyList<string> Paragraphs);

public static class GccResearchCaps
{
    public const int MaxQuoteables = 3;
    public const int MaxHeadingsPerPage = 8;
    public const int MaxParagraphsPerPage = 6;
    public const int MaxHeadingChars = 200;
    public const int MaxParagraphChars = 500;
    public const int MaxTitleChars = 300;
    public const int MaxOrganicTitleChars = 200;
    public const int MaxPaaChars = 300;
    public const int MaxRelatedChars = 200;
}
