namespace GeekApplication.Models.Blog;

public sealed record PostSectionInput(
    int SortOrder,
    string? HeadingTag,
    string? HeadingText,
    string BodyContent,
    string? MediaUrl,
    string? MediaAlt);

public sealed class UpsertBlogPostCommand
{
    public string PostType { get; init; } = "Blog";
    public string SchemaType { get; init; } = "BlogPosting";
    public bool IsPublished { get; init; }
    public string LanguageCode { get; init; } = "en";
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? MetaDescription { get; init; }
    public string MainSummary { get; init; } = string.Empty;
    public string HeroSummary { get; init; } = string.Empty;
    public string BlogSummary { get; init; } = string.Empty;
    public string AdvertisingSummary { get; init; } = string.Empty;
    public string? JsonLdOverride { get; init; }
    public IReadOnlyList<string> TagSlugs { get; init; } = [];
    public int? AuthorId { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string CategorySlug { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string>? Presentation { get; init; }
    public string? CwJobId { get; init; }
    public IReadOnlyList<PostSectionInput> Sections { get; init; } = [];
}
