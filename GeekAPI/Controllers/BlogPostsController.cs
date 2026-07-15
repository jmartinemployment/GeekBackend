using System.Text.Json;
using GeekAPI.Dtos;
using GeekApplication.Blog;
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
    private readonly IBlogRepository _blog;
    private readonly IAssetUploadService _assetUploads;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

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

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetCategories(
        [FromQuery] string? lang,
        CancellationToken ct = default)
    {
        var categories = await _blog.GetCategoriesAsync(lang, ct);
        return Ok(categories);
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

        var commentId = await _blog.InsertCommentReplyWithoutLocalTransactionAsync(
            postId,
            request.UserId,
            request.Content,
            request.ParentPath,
            attachmentUrl,
            ct);

        var thread = await _blog.GetThreadedCommentsAsync(postId, ct);
        var created = thread.FirstOrDefault(c => c.Id == commentId);

        if (created is null)
            return StatusCode(StatusCodes.Status201Created, new { id = commentId });

        return CreatedAtAction(nameof(GetPost), new { lang = request.LanguageCode, slug = request.PostSlug }, MapComment(created));
    }

    private static UpsertBlogPostCommand ToCommand(BlogPostRequest request)
    {
        var (title, jsonLdOverride) = NormalizeUseCaseTitle(request.Slug, request.Title, request.JsonLdOverride);

        return new UpsertBlogPostCommand
        {
            PostType = request.PostType,
            SchemaType = request.SchemaType,
            IsPublished = request.IsPublished,
            LanguageCode = request.LanguageCode,
            Slug = request.Slug,
            Title = title,
            Summary = request.Summary,
            MetaDescription = request.MetaDescription,
            MainSummary = request.MainSummary,
            BlogSummary = request.BlogSummary,
            AdvertisingSummary = request.AdvertisingSummary,
            JsonLdOverride = jsonLdOverride,
            Sections = request.Sections
                .Select(s => new PostSectionInput(s.SortOrder, s.HeadingTag, s.HeadingText, s.BodyContent, s.MediaUrl, s.MediaAlt))
                .ToList(),
            TagSlugs = request.TagSlugs,
            AuthorId = request.AuthorId,
            PublishedAt = request.PublishedAt,
            CategorySlug = request.CategorySlug,
            Presentation = PostPresentationFields.Normalize(request.Presentation),
            CwJobId = request.CwJobId,
        };
    }

    private static (string Title, string? JsonLdOverride) NormalizeUseCaseTitle(
        string slug,
        string title,
        string? jsonLdOverride)
    {
        if (!UseCasePostNormalizer.IsUseCaseSlug(slug))
            return (title, jsonLdOverride);

        var (normalizedTitle, schema) = UseCasePostNormalizer.Normalize(slug, title, jsonLdOverride ?? "{}");
        var normalizedJsonLd = jsonLdOverride is null && schema == "{}" ? null : schema;
        return (normalizedTitle, normalizedJsonLd);
    }

    private static Dictionary<string, string> ParsePresentation(string? presentationJson)
    {
        if (string.IsNullOrWhiteSpace(presentationJson))
            return new Dictionary<string, string>();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(presentationJson)
               ?? new Dictionary<string, string>();
    }

    private static IReadOnlyList<PostSectionDto> ParseSections(string sectionsJson)
    {
        if (string.IsNullOrWhiteSpace(sectionsJson))
            return [];

        return JsonSerializer.Deserialize<List<PostSectionDto>>(sectionsJson, JsonOptions) ?? [];
    }

    private static BlogPostResponse MapToResponse(BlogPostFlatDto flat) =>
        new()
        {
            PostId = flat.PostId,
            PostType = flat.PostType,
            SchemaType = flat.SchemaType,
            LanguageCode = flat.LanguageCode,
            Slug = flat.Slug,
            Title = flat.Title,
            Summary = flat.Summary,
            MetaDescription = flat.MetaDescription,
            MainSummary = flat.MainSummary,
            BlogSummary = flat.BlogSummary,
            AdvertisingSummary = flat.AdvertisingSummary,
            Sections = ParseSections(flat.SectionsJson),
            PublishedAt = flat.PublishedAt,
            LocalizedTagsJson = flat.LocalizedTagsJson,
            JsonLd = SchemaMetadataParser.Parse(flat.SchemaType, flat.JsonLdOverride ?? "{}"),
            JsonLdOverride = flat.JsonLdOverride,
            CategoryId = flat.CategoryId,
            CategorySlug = flat.CategorySlug,
            Presentation = ParsePresentation(flat.PresentationJson),
            CwJobId = flat.CwJobId,
        };

    private static BlogPostAdminResponse MapToAdminResponse(BlogPostFlatDto flat) =>
        new()
        {
            PostId = flat.PostId,
            PostType = flat.PostType,
            SchemaType = flat.SchemaType,
            LanguageCode = flat.LanguageCode,
            Slug = flat.Slug,
            Title = flat.Title,
            Summary = flat.Summary,
            MetaDescription = flat.MetaDescription,
            MainSummary = flat.MainSummary,
            BlogSummary = flat.BlogSummary,
            AdvertisingSummary = flat.AdvertisingSummary,
            Sections = ParseSections(flat.SectionsJson),
            IsPublished = flat.IsPublished,
            PublishedAt = flat.PublishedAt,
            CreatedAt = flat.CreatedAt,
            UpdatedAt = flat.UpdatedAt,
            LocalizedTagsJson = flat.LocalizedTagsJson,
            JsonLd = SchemaMetadataParser.Parse(flat.SchemaType, flat.JsonLdOverride ?? "{}"),
            JsonLdOverride = flat.JsonLdOverride,
            CategoryId = flat.CategoryId,
            CategorySlug = flat.CategorySlug,
            Presentation = ParsePresentation(flat.PresentationJson),
            CwJobId = flat.CwJobId,
        };

    private static CommentResponse MapComment(CommentDto comment) =>
        new(comment.Id, comment.PostId, comment.UserId, comment.Content, comment.AttachmentUrl, comment.Path, comment.Depth, comment.CreatedAt);
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
