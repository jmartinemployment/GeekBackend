namespace GeekApplication.Models.ContentCreator;

/// <summary>
/// Persisted research on a Content Creator create. Nothing in it is cut by a count: every page, every
/// passage and every heading a search brought back is kept, and what a request carries of it is
/// limited only by what the request's model accepts.
/// </summary>
public sealed record GccResearchDocument(
    GccSerpIndex? SerpIndex,
    IReadOnlyList<GccQuoteablePage> Quoteables,
    IReadOnlyList<GccKeywordSource>? Sources = null,
    IReadOnlyList<GccParsedSerpPage>? SerpPages = null,
    /// <summary>
    /// Retrieved competitor pages, kept apart from <see cref="Quoteables"/> rather than tagged
    /// inside it.
    ///
    /// <para>
    /// A rival is read, never cited. One list would render competitor prose under a header whose
    /// rules say "name the source where the claim appears, and carry its URL", and would hand
    /// competitor pages to the partner extractor and to the <c>SoftwareApplication.url</c>
    /// derivation — both of which read <see cref="Quoteables"/> wholesale. A separate list leaves
    /// every existing consumer correct by construction; a tag would need each one found and
    /// filtered.
    /// </para>
    ///
    /// <para>
    /// Optional with a default, so research written before competitors were retrieved deserializes
    /// as null and round-trips unchanged.
    /// </para>
    /// </summary>
    IReadOnlyList<GccQuoteablePage>? CompetitorQuoteables = null,
    /// <summary>
    /// Retrieved pages from the publisher's own site. A third list because the instruction attached
    /// to them is a third one: partner evidence is cited, competitor evidence is never cited, and
    /// the publisher's own pages are what this piece must not repeat.
    /// </summary>
    IReadOnlyList<GccQuoteablePage>? SiteQuoteables = null,
    /// <summary>
    /// What the publisher states on their own site page, by heading -- The Methodology, Seamless
    /// Integrations, Artificial Intelligence Use Cases, whatever the page carries. Read from the site's
    /// crawl page itself, never retrieved by a keyword question, and replaced on every run. The page
    /// argues these as the publisher's own (Jeff, 2026-10-06: "writer needs to use my The Methodology
    /// instead of inventing its own").
    /// </summary>
    IReadOnlyList<GccPublisherPosition>? PublisherPositions = null);

/// <summary>
/// What a search scored one passage: the library's score for the hit, and the reranking model's own
/// when the search was reranked. For the run's record, where a cut-off is read from real scores before
/// one is set; no page is written from it and nothing is filtered on it.
/// </summary>
public sealed record GccPassageScore(double Score, double? Reranked = null);

/// <summary>
/// One search of a crawl, as it is kept on a page it found a passage on: what kind of search it was and
/// what it asked. For choosing which passages a request carries, for the run's record, and for the
/// line under each passage that tells a writer what the passage was found for.
/// </summary>
/// <remarks>
/// A partner's crawl is searched with the brief: the problem, then one search per row of failure and
/// remedy. Until 2026-10-11 which search found which passage was dropped where the results came back,
/// so nothing downstream could tell a passage found for one failure from a passage found for another.
/// </remarks>
/// <param name="Kind">
/// <see cref="KindProblem"/> for the brief's statement of the problem, <see cref="KindRow"/> for one of
/// its rows of failure and remedy, <see cref="KindKeyword"/> for the project's keyword, which is what a
/// crawl is asked when the brief says nothing of it.
/// </param>
/// <param name="Asked">What the meaning half of the search was sent.</param>
/// <param name="Failure">For a row, the failure the operator entered on it; null when the row has none.</param>
/// <param name="Terms">What the keyword half of the search was sent; null when it was sent nothing.</param>
public sealed record GccPassageSearch(string Kind, string Asked, string? Failure = null, string? Terms = null)
{
    public const string KindProblem = "problem";
    public const string KindRow = "row";
    public const string KindKeyword = "keyword";
}

/// <summary>One position the publisher states on their own site: a named section and its text.</summary>
/// <param name="Heading">The section's heading, as written on the site.</param>
/// <param name="Paragraphs">The section's text, one entry per block, in order; a sub-heading is an entry.</param>
/// <param name="Url">The page the section was read from.</param>
public sealed record GccPublisherPosition(string Heading, IReadOnlyList<string> Paragraphs, string Url);

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

/// <summary>A real crawled/parsed heading with its HTML level (1 = h1 ... 6 = h6).</summary>
public sealed record HeadingDto(int Level, string Text);

public sealed record GccQuoteablePage(
    string Url,
    string Title,
    IReadOnlyList<HeadingDto> Headings,
    IReadOnlyList<string> Paragraphs,
    string? PageId = null,
    string? SectionTitle = null,
    /// <summary>Authorized crawl run that produced this page (Geek-Crawler or project-site).</summary>
    string? RunId = null,
    /// <summary><c>rag_chunk</c> from Geek-Crawler-Rag index; <c>seed_html</c> from crawl HTML extract.</summary>
    string? RetrievalMode = null,
    /// <summary>SHA-256 hex of source HTML or concatenated paragraph text used for extract.</summary>
    string? SourceDigest = null,
    DateTimeOffset? CrawledAtUtc = null,
    /// <summary>
    /// What the search scored each of <see cref="Paragraphs"/>, in the same order. Null on a page that
    /// did not come from a search, and never read by a writer: see <see cref="GccPassageScore"/>.
    /// </summary>
    IReadOnlyList<GccPassageScore>? Scores = null,
    /// <summary>
    /// The kind of page the index marked this as when it was indexed: <c>pricing</c>, <c>docs</c>,
    /// <c>blog</c>, <c>product</c>, <c>about</c>, <c>careers</c>, <c>legal</c>, <c>compare</c>, or
    /// <c>page</c> for none of those. Lower case. Null on a page that did not come from a search, or
    /// when the index did not say. Never printed to a writer.
    /// </summary>
    string? Category = null,
    /// <summary>
    /// The searches that found a passage on this page, each once, in the order they first found one.
    /// The page's own list, so a page kept from one run to the next keeps what its
    /// <see cref="FoundBy"/> points at. Null on a page that did not come from a search.
    /// </summary>
    IReadOnlyList<GccPassageSearch>? Searches = null,
    /// <summary>
    /// Which of <see cref="Searches"/> found each of <see cref="Paragraphs"/>, in the same order: one
    /// list of positions in <see cref="Searches"/> for each passage. Null on a page that did not come
    /// from a search. Read through <see cref="SearchesThatFound"/>, which answers nothing when the two
    /// lists are out of step.
    /// </summary>
    IReadOnlyList<IReadOnlyList<int>>? FoundBy = null)
{
    public const string RetrievalModeRagChunk = "rag_chunk";
    public const string RetrievalModeSeedHtml = "seed_html";

    /// <summary>
    /// The searches that found the passage at <paramref name="paragraph"/>, or none when the page does
    /// not say: it did not come from a search, or its lists are not in step with its passages.
    /// </summary>
    public IReadOnlyList<GccPassageSearch> SearchesThatFound(int paragraph)
    {
        if (Searches is null || FoundBy is null || Paragraphs is null || FoundBy.Count != Paragraphs.Count) return [];
        if (paragraph < 0 || paragraph >= FoundBy.Count) return [];

        // Read back from stored JSON, a passage's list or a search in it can be missing.
        var found = new List<GccPassageSearch>();
        foreach (var position in FoundBy[paragraph] ?? [])
        {
            if (position >= 0 && position < Searches.Count && Searches[position] is { } search) found.Add(search);
        }

        return found;
    }
}

/// <summary>
/// Settings for reading partner pages. The per-page limits here (headings, passages, the length of
/// one, the length of a title, characters a page) bound what <c>GccArticleHtmlExtractor</c> takes out
/// of a fetched page's layout, and nothing else: no search result is cut by them. Until 2026-10-12
/// three of them also cut what a search of the index returned (eighty passages a page, 2,000
/// characters a passage, 300 a title); what a request carries of a search's results is limited only
/// by what its model accepts.
/// </summary>
public static class GccPartnerResearchCaps
{
    public const int MaxHeadingsPerPage = 60;
    public const int MaxParagraphsPerPage = 80;
    public const int MaxHeadingChars = 300;
    public const int MaxParagraphChars = 2000;
    public const int MaxTitleChars = 300;
    /// <summary>
    /// Quality floor for a retrieved chunk, applied by Geek-Crawler-Rag before ranking.
    ///
    /// <para>
    /// 0.55 is deliberately permissive: this exists to drop navigation, cookie notices and footer
    /// fragments, not to second-guess the writer about what is interesting. A floor set high enough
    /// to be opinionated would quietly starve pages whose crawl scored modestly across the board,
    /// and a retrieval that returns nothing is worse than one that returns something plain.
    /// </para>
    /// </summary>
    public const double MinChunkQuality = 0.55;
    /// <summary>
    /// Soft stop once the cleaned title, headings and paragraphs of a fetched page reach this many
    /// characters. It bounds what <c>GccArticleHtmlExtractor</c> takes out of a page layout and is
    /// never applied to what a search of the index returns.
    /// </summary>
    public const int MaxCharsPerPage = 16_000;
    public const int MaxHtmlBytes = 2_000_000;
    public const int FetchTimeoutSeconds = 15;
    /// <summary>Default polite spacer between requests to the same host (seconds).</summary>
    public const int DefaultHostDelaySeconds = 12;
    /// <summary>Max in-flight HTTP fetches across all hosts (per-host still serial).</summary>
    public const int MaxConcurrentFetches = 4;
    /// <summary>Deprecated alias — use <see cref="MaxConcurrentFetches"/>.</summary>
    public const int MaxConcurrency = MaxConcurrentFetches;
    /// <summary>Reuse a successful partner crawl for the same URL within this window.</summary>
    public const int CacheFreshnessHours = 24;

    public const string BotName = "geekatyourspotbot";
    public const string BotContactEmail = "jeffm@geekatyourspot.com";
    public const string BotContactUrl = "https://geekatyourspot.com";

    public static string UserAgent { get; } =
        $"{BotName}/1.0 (+mailto:{BotContactEmail}; +{BotContactUrl})";
}
