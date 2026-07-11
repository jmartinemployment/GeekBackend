using System.Text.Json.Serialization;
using GeekApplication.Models.Blog;

namespace GeekAPI.Dtos;

public sealed class BlogPostResponse
{
    public int PostId { get; init; }
    public string PostType { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; init; }
    public string LocalizedTagsJson { get; init; } = "[]";

    [JsonPropertyName("jsonLd")]
    public ArticleMetadata? JsonLd { get; init; }
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
    string Path,
    int Depth,
    DateTimeOffset CreatedAt);
