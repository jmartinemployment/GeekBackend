namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// Persisted research on a Content Creator create.
/// Caps: ≤3 quoteables; ≤8 headings / ≤6 paragraphs per page; heading ≤200 chars; paragraph ≤500 chars.
/// </summary>
public sealed record GccResearchDocument(
    GccSerpIndex? SerpIndex,
    IReadOnlyList<GccQuoteablePage> Quoteables,
    IReadOnlyList<GccKeywordSource>? Sources = null);

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
