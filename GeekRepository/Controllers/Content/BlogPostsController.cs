using GeekApplication.Interfaces;
using GeekApplication.Models.Blog;
using GeekRepository.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Content;

[ApiController]
[Route("repo/content/blog")]
public sealed class BlogPostsController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBlogRepository _blog;

    public BlogPostsController(IUnitOfWork unitOfWork, IBlogRepository blog)
    {
        _unitOfWork = unitOfWork;
        _blog = blog;
    }

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<BlogPostFlatDto>>> Search(
        [FromQuery] string q,
        [FromQuery] string lang = "en",
        CancellationToken ct = default)
    {
        IReadOnlyList<BlogPostFlatDto> results = [];

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            results = await _blog.SearchPostsWithOptimizedPlanAsync(q, lang, ct);
        }, ct);

        return Ok(results);
    }

    [HttpGet("all")]
    public async Task<ActionResult<IReadOnlyList<BlogPostFlatDto>>> GetAll(
        [FromQuery] string? lang,
        [FromQuery] string? status,
        [FromQuery] string? postType,
        CancellationToken ct = default)
    {
        IReadOnlyList<BlogPostFlatDto> results = [];

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            results = await _blog.GetAllPostsAsync(lang, status, postType, ct);
        }, ct);

        return Ok(results);
    }

    [HttpGet("by-id/{postId:int}")]
    public async Task<ActionResult<BlogPostFlatDto>> GetById(
        int postId,
        [FromQuery] string? lang,
        CancellationToken ct = default)
    {
        BlogPostFlatDto? post = null;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            post = await _blog.GetPostByIdAsync(postId, lang, ct);
        }, ct);

        return post is null ? NotFound() : Ok(post);
    }

    [HttpGet("technical/{lang}")]
    public async Task<ActionResult<IReadOnlyList<BlogPostFlatDto>>> GetTechnicalArticles(
        string lang,
        CancellationToken ct = default)
    {
        IReadOnlyList<BlogPostFlatDto> articles = [];

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            articles = await _blog.GetTechnicalArticlesOnlyAsync(lang, ct);
        }, ct);

        return Ok(articles);
    }

    [HttpPost]
    public async Task<ActionResult<BlogPostFlatDto>> Create(
        [FromBody] UpsertBlogPostCommand command,
        CancellationToken ct = default)
    {
        int postId = 0;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            postId = await _blog.CreatePostAsync(command, ct);
        }, ct);

        BlogPostFlatDto? created = null;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            created = await _blog.GetPostByIdAsync(postId, command.LanguageCode, ct);
        }, ct);

        return CreatedAtAction(nameof(GetById), new { postId }, created);
    }

    [HttpPut("{postId:int}")]
    public async Task<ActionResult<BlogPostFlatDto>> Update(
        int postId,
        [FromBody] UpsertBlogPostCommand command,
        CancellationToken ct = default)
    {
        bool updated = false;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            updated = await _blog.UpdatePostAsync(postId, command, ct);
        }, ct);

        if (!updated) return NotFound();

        BlogPostFlatDto? post = null;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            post = await _blog.GetPostByIdAsync(postId, command.LanguageCode, ct);
        }, ct);

        return Ok(post);
    }

    [HttpDelete("{postId:int}")]
    public async Task<IActionResult> Delete(int postId, CancellationToken ct = default)
    {
        bool deleted = false;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            deleted = await _blog.DeletePostAsync(postId, ct);
        }, ct);

        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("{postId:int}/comments")]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> GetComments(
        int postId,
        CancellationToken ct = default)
    {
        IReadOnlyList<CommentDto> comments = [];

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            comments = await _blog.GetThreadedCommentsAsync(postId, ct);
        }, ct);

        return Ok(comments);
    }

    [HttpPost("{postId:int}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(
        int postId,
        [FromBody] AddCommentRequest request,
        CancellationToken ct = default)
    {
        if (!await UserHasRoleAsync(request.UserId, "reader", ct))
            return Forbid();

        int commentId = 0;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            commentId = await _blog.InsertCommentReplyWithoutLocalTransactionAsync(
                postId,
                request.UserId,
                request.Content,
                request.ParentPath,
                request.AttachmentUrl,
                ct);
        }, ct);

        CommentDto? created = null;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            var thread = await _blog.GetThreadedCommentsAsync(postId, ct);
            created = thread.FirstOrDefault(c => c.Id == commentId);
        }, ct);

        return created is null
            ? StatusCode(StatusCodes.Status201Created, new { id = commentId })
            : CreatedAtAction(nameof(GetComments), new { postId }, created);
    }

    [HttpGet("{lang}/{**slug}")]
    public async Task<ActionResult<BlogPostFlatDto>> GetBySlug(
        string lang,
        string slug,
        CancellationToken ct = default)
    {
        BlogPostFlatDto? post = null;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            post = await _blog.GetPostBySlugAsync(slug, lang, ct);
        }, ct);

        return post is null ? NotFound() : Ok(post);
    }

    private async Task<bool> UserHasRoleAsync(int userId, string roleName, CancellationToken ct)
    {
        var allowed = false;

        await _unitOfWork.ExecuteInResilientTransactionAsync(async () =>
        {
            allowed = await _blog.UserHasRoleAsync(userId, roleName, ct);
        }, ct);

        return allowed;
    }
}

public sealed record AddCommentRequest(
    int UserId,
    string Content,
    string? ParentPath,
    string? AttachmentUrl = null);
