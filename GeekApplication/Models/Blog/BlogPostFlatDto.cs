namespace GeekApplication.Models.Blog;

public sealed class BlogPostFlatDto
{
    public int PostId { get; init; }
    public string PostType { get; init; } = "BlogPosting";
    public string LanguageCode { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

  /// <summary>JSON array of localized tag objects: [{ "slug": "...", "name": "..." }]</summary>
    public string LocalizedTagsJson { get; init; } = "[]";

  /// <summary>Raw JSONB payload from geek_blog.post_translations.schema_metadata.</summary>
    public string SchemaMetadataJson { get; init; } = "{}";

    public float? SearchRank { get; init; }
}
