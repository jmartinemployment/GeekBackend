using GeekAPI.Services.Workflow.DTOs;

namespace GeekAPI.Services.Workflow.Services.SchemaBuilders;

/// <summary>
/// The nodes every document shares: who wrote it, who published it, and the questions it answers.
///
/// <para>
/// These were written three times each -- identical bodies and identical doc comments in
/// ArticleSchemaBuilder, BlogPostingSchemaBuilder and SoftwareApplicationSchemaBuilder. The author
/// node was pulled into one place on 2026-09-23 while the publisher and FAQ copies sitting in the
/// same methods were left alone, which is the half-done version of this fix and is what prompted
/// Jeff to ask what had been done half-assed about areaServed: the areaServed rule lives inside
/// BuildPublisher, so it existed in triplicate.
/// </para>
///
/// <para>
/// One home matters here specifically because these nodes describe the same real entity on every
/// page. Three copies is three chances for one page's publisher to stop matching another's.
/// </para>
/// </summary>
internal static class SchemaNodes
{
    /// <summary>
    /// The author. Organization, not Person: the configured author is an editorial team, and typing
    /// a team as a Person sends a search engine looking for a human of that name and finding
    /// nobody, so the authorship signal resolves to no entity at all (Jeff, 2026-09-23).
    /// </summary>
    public static Dictionary<string, object?> BuildAuthor(ContentMetadata metadata) =>
        new()
        {
            ["@type"] = "Organization",
            ["name"] = metadata.AuthorName,
        };

    /// <summary>
    /// The publisher. Its <c>@type</c> mirrors what the client's own markup declares — never
    /// inferred — and <c>areaServed</c> appears only when the site actually declares service areas.
    /// An empty array would assert "serves nowhere", so absent means absent.
    /// </summary>
    /// <remarks>
    /// This node describes the publisher, not the page's subject. That distinction was lost for a
    /// while: tool pages suppressed both areaServed and the declared publisher type on the grounds
    /// that "a partner page must not assert the operator's own geography" -- but the publisher of a
    /// page about a partner's product is still the operator, and its service areas are still true.
    /// The effect was that the revenue-critical content type shipped a generic Organization with no
    /// geography while pillar and blog carried the real one.
    /// </remarks>
    public static Dictionary<string, object?> BuildPublisher(ContentMetadata metadata)
    {
        var publisher = new Dictionary<string, object?>
        {
            ["@type"] = string.IsNullOrWhiteSpace(metadata.PublisherType)
                ? "Organization"
                : metadata.PublisherType,
            ["name"] = metadata.PublisherName,
            ["logo"] = new Dictionary<string, object?>
            {
                ["@type"] = "ImageObject",
                ["url"] = metadata.PublisherLogoUrl
            }
        };

        var areas = metadata.AreaServed?
            .Where(area => !string.IsNullOrWhiteSpace(area))
            .Select(area => area.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (areas is { Count: > 0 })
        {
            publisher["areaServed"] = areas;
        }

        return publisher;
    }

    /// <summary>
    /// The <c>FAQPage</c> node for questions the page actually contains, or null when it answers
    /// none.
    /// </summary>
    /// <remarks>
    /// Google restricted FAQ <i>rich results</i> to authoritative government and health sites in
    /// August 2023, so this does not render as a SERP feature for most sites. The markup is emitted
    /// for machine consumption — answer engines and entity understanding — where
    /// question-to-answer adjacency is the point. Never emit an entry the page does not answer.
    /// </remarks>
    public static Dictionary<string, object?>? BuildFaqPage(ContentMetadata metadata)
    {
        var entries = metadata.Faq?
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Question)
                            && !string.IsNullOrWhiteSpace(entry.Answer))
            .ToList();
        if (entries is not { Count: > 0 })
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["@type"] = "FAQPage",
            ["mainEntity"] = entries.Select(entry => new Dictionary<string, object?>
            {
                ["@type"] = "Question",
                ["name"] = entry.Question.Trim(),
                ["acceptedAnswer"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Answer",
                    ["text"] = entry.Answer.Trim()
                }
            }).ToList()
        };
    }
}
