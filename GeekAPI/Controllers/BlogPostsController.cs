using System.Text.Json;
using GeekAPI.Dtos;
using GeekApplication.Interfaces;
using GeekApplication.Models.Blog;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers;

/// <summary>
/// Blog content API: public reads and admin CRUD (writes require X-API-Key).
/// </summary>
[ApiController]
[Route("api/blog")]
public sealed class BlogPostsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonLdOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IBlogRepository _blog;
    private readonly IAssetUploadService _assetUploads;

    public BlogPostsController(IBlogRepository blog, IAssetUploadService assetUploads)
    {
        _blog = blog;
        _assetUploads = assetUploads;
    }

    [HttpGet("all")]
    public async Task<ActionResult<IReadOnlyList<BlogPostAdminResponse>>> GetAll(
        [FromQuery] string? lang,
        [FromQuery] string? status,
        [FromQuery] string? postType,
        CancellationToken ct = default)
    {
        var posts = await _blog.GetAllPostsAsync(lang, status, postType, ct);
        return Ok(posts.Select(MapToAdminResponse).ToList());
    }

    [HttpGet("by-id/{id:int}")]
    public async Task<ActionResult<BlogPostAdminResponse>> GetById(
        int id,
        [FromQuery] string? lang,
        CancellationToken ct = default)
    {
        var post = await _blog.GetPostByIdAsync(id, lang, ct);
        return post is null ? NotFound() : Ok(MapToAdminResponse(post));
    }

    [HttpPost]
    public async Task<ActionResult<BlogPostAdminResponse>> Create(
        [FromBody] BlogPostRequest request,
        CancellationToken ct = default)
    {
        var postId = await _blog.CreatePostAsync(ToCommand(request), ct);
        var created = await _blog.GetPostByIdAsync(postId, request.LanguageCode, ct);
        return CreatedAtAction(nameof(GetById), new { id = postId }, MapToAdminResponse(created!));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<BlogPostAdminResponse>> Update(
        int id,
        [FromBody] BlogPostRequest request,
        CancellationToken ct = default)
    {
        if (!await _blog.UpdatePostAsync(id, ToCommand(request), ct))
            return NotFound();

        var updated = await _blog.GetPostByIdAsync(id, request.LanguageCode, ct);
        return Ok(MapToAdminResponse(updated!));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        return await _blog.DeletePostAsync(id, ct) ? NoContent() : NotFound();
    }

    [HttpGet("{lang}/{**slug}")]
    public async Task<ActionResult<BlogPostResponse>> GetPost(
        string lang,
        string slug,
        CancellationToken ct = default)
    {
        var flat = await _blog.GetPostBySlugAsync(slug, lang, ct);
        if (flat is null)
            return NotFound();

        return Ok(MapToResponse(flat));
    }

    [HttpPost("{postId:int}/comments")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<CommentResponse>> Reply(
        int postId,
        [FromForm] CommentReplyFormRequest request,
        CancellationToken ct = default)
    {
        string? attachmentUrl = null;
        if (request.Attachment is not null)
            attachmentUrl = await _assetUploads.UploadAsync(request.Attachment, ct);

        var content = attachmentUrl is null
            ? request.Content
            : $"{request.Content}\n\n![attachment]({attachmentUrl})";

        var commentId = await _blog.InsertCommentReplyWithoutLocalTransactionAsync(
            postId,
            request.UserId,
            content,
            request.ParentPath,
            ct);

        var thread = await _blog.GetThreadedCommentsAsync(postId, ct);
        var created = thread.FirstOrDefault(c => c.Id == commentId);

        if (created is null)
            return StatusCode(StatusCodes.Status201Created, new { id = commentId });

        return CreatedAtAction(nameof(GetPost), new { lang = request.LanguageCode, slug = request.PostSlug }, MapComment(created));
    }

    private static UpsertBlogPostCommand ToCommand(BlogPostRequest request) =>
        new()
        {
            PostType = request.PostType,
            Status = request.Status,
            LanguageCode = request.LanguageCode,
            Slug = request.Slug,
            Title = request.Title,
            Body = request.Body,
            SchemaMetadataJson = request.SchemaMetadataJson,
            TagSlugs = request.TagSlugs,
            AuthorId = request.AuthorId,
            PublishedAt = request.PublishedAt
        };

    private static BlogPostResponse MapToResponse(BlogPostFlatDto flat) =>
        new()
        {
            PostId = flat.PostId,
            PostType = flat.PostType,
            LanguageCode = flat.LanguageCode,
            Slug = flat.Slug,
            Title = flat.Title,
            Body = flat.Body,
            PublishedAt = flat.PublishedAt,
            LocalizedTagsJson = flat.LocalizedTagsJson,
            JsonLd = DeserializeSchemaMetadata(flat.PostType, flat.SchemaMetadataJson)
        };

    private static BlogPostAdminResponse MapToAdminResponse(BlogPostFlatDto flat) =>
        new()
        {
            PostId = flat.PostId,
            PostType = flat.PostType,
            LanguageCode = flat.LanguageCode,
            Slug = flat.Slug,
            Title = flat.Title,
            Body = flat.Body,
            PublishedAt = flat.PublishedAt,
            LocalizedTagsJson = flat.LocalizedTagsJson,
            JsonLd = DeserializeSchemaMetadata(flat.PostType, flat.SchemaMetadataJson),
            Status = flat.Status,
            CreatedAt = flat.CreatedAt,
            UpdatedAt = flat.UpdatedAt,
            SchemaMetadataJson = flat.SchemaMetadataJson
        };

    private static ArticleMetadata? DeserializeSchemaMetadata(string postType, string schemaMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(schemaMetadataJson) || schemaMetadataJson == "{}")
            return null;

        try
        {
            return postType switch
            {
                "TechnicalArticle" => JsonSerializer.Deserialize<TechnicalArticleMetadata>(schemaMetadataJson, JsonLdOptions),
                "BlogPosting" => JsonSerializer.Deserialize<BlogPostingMetadata>(schemaMetadataJson, JsonLdOptions),
                _ => null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static CommentResponse MapComment(CommentDto comment) =>
        new(comment.Id, comment.PostId, comment.UserId, comment.Content, comment.Path, comment.Depth, comment.CreatedAt);
}

public interface IAssetUploadService
{
    Task<string> UploadAsync(IFormFile file, CancellationToken ct = default);
}

public sealed class NoOpAssetUploadService : IAssetUploadService
{
    public Task<string> UploadAsync(IFormFile file, CancellationToken ct = default) =>
        Task.FromResult($"https://assets.example.com/{Guid.NewGuid():N}/{file.FileName}");
}
