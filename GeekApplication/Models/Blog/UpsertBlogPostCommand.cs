namespace GeekApplication.Models.Blog;

public sealed class UpsertBlogPostCommand
{
    public string PostType { get; init; } = "BlogPosting";
    public string Status { get; init; } = "draft";
    public string LanguageCode { get; init; } = "en";
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string SchemaMetadataJson { get; init; } = "{}";
    public IReadOnlyList<string> TagSlugs { get; init; } = [];
    public int? AuthorId { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string? DepartmentSlug { get; init; }
    public IReadOnlyDictionary<string, string>? Presentation { get; init; }
    public string? HeroImageUrl { get; init; }
    public Guid? SourceProjectId { get; init; }
    public string? ContentRole { get; init; }
    public string? SourcePillarSlug { get; init; }
}
