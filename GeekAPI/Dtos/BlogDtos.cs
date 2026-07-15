using System.Text.Json.Serialization;
using GeekApplication.Models.Blog;

namespace GeekAPI.Dtos;

public sealed record PostSectionDto(
    int SortOrder,
    string? HeadingTag,
    string? HeadingText,
    string BodyContent,
    string? MediaUrl,
    string? MediaAlt);

public sealed class BlogPostResponse
{
    public int PostId { get; init; }
    public string PostType { get; init; } = string.Empty;
    public string SchemaType { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? MetaDescription { get; init; }
    public string MainSummary { get; init; } = string.Empty;
    public string BlogSummary { get; init; } = string.Empty;
    public string AdvertisingSummary { get; init; } = string.Empty;
    public IReadOnlyList<PostSectionDto> Sections { get; init; } = [];
    public DateTimeOffset? PublishedAt { get; init; }
    public string LocalizedTagsJson { get; init; } = "[]";

    [JsonPropertyName("jsonLd")]
    public ArticleMetadata? JsonLd { get; init; }

    /// <summary>Stored schema.org JSON-LD override (flat or @graph) for script emission.</summary>
    public string? JsonLdOverride { get; init; }

    public int CategoryId { get; init; }
    public string CategorySlug { get; init; } = string.Empty;
    public Dictionary<string, string> Presentation { get; init; } = new();
    public string? CwJobId { get; init; }
}

public sealed record CommentReplyRequest(
    int UserId,
    string Content,
    string? ParentPath,
    string LanguageCode,
    string PostSlug);

public sealed class CommentReplyFormRequest
{
    public int UserId { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? ParentPath { get; init; }
    public string LanguageCode { get; init; } = "en";
    public string PostSlug { get; init; } = string.Empty;
    public IFormFile? Attachment { get; init; }
}

public sealed record CommentResponse(
    int Id,
    int PostId,
    int? UserId,
    string Content,
    string? AttachmentUrl,
    string Path,
    int Depth,
    DateTimeOffset CreatedAt);

public sealed class BlogPostRequest
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
    public string BlogSummary { get; init; } = string.Empty;
    public string AdvertisingSummary { get; init; } = string.Empty;
    public string? JsonLdOverride { get; init; }
    public IReadOnlyList<PostSectionDto> Sections { get; init; } = [];
    public IReadOnlyList<string> TagSlugs { get; init; } = [];
    public int? AuthorId { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string CategorySlug { get; init; } = string.Empty;
    public Dictionary<string, string>? Presentation { get; init; }
    public string? CwJobId { get; init; }
}

public sealed class BlogPostAdminResponse
{
    public int PostId { get; init; }
    public string PostType { get; init; } = string.Empty;
    public string SchemaType { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? MetaDescription { get; init; }
    public string MainSummary { get; init; } = string.Empty;
    public string BlogSummary { get; init; } = string.Empty;
    public string AdvertisingSummary { get; init; } = string.Empty;
    public IReadOnlyList<PostSectionDto> Sections { get; init; } = [];
    public bool IsPublished { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string LocalizedTagsJson { get; init; } = "[]";
    public string? JsonLdOverride { get; init; }

    [JsonPropertyName("jsonLd")]
    public ArticleMetadata? JsonLd { get; init; }

    public int CategoryId { get; init; }
    public string CategorySlug { get; init; } = string.Empty;
    public Dictionary<string, string> Presentation { get; init; } = new();
    public string? CwJobId { get; init; }
}
