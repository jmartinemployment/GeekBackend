namespace GeekApplication.Models.Blog;

public sealed class BlogPostFlatDto
{
    public int PostId { get; init; }
    public string PostType { get; init; } = "Blog";
    public string LanguageCode { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public string SchemaType { get; init; } = "BlogPosting";
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>JSON array of localized tag objects: [{ "slug": "...", "name": "..." }]</summary>
    public string LocalizedTagsJson { get; init; } = "[]";

    public int CategoryId { get; init; }
    public string CategorySlug { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;
    public string? MetaDescription { get; init; }
    public string MainSummary { get; init; } = string.Empty;
    public string BlogSummary { get; init; } = string.Empty;
    public string AdvertisingSummary { get; init; } = string.Empty;

    /// <summary>Raw JSON-LD payload from geek_blog.post_translations.json_ld_override (flat or @graph).</summary>
    public string? JsonLdOverride { get; init; }

    public string? CwJobId { get; init; }

    /// <summary>JSON array of geek_blog.post_sections rows, ordered by sort_order.</summary>
    public string SectionsJson { get; init; } = "[]";

    /// <summary>JSON object of attribute_key → attribute_value from geek_blog.post_presentation_attributes.</summary>
    public string PresentationJson { get; init; } = "{}";

    public float? SearchRank { get; init; }
}
