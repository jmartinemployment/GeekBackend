using GeekApplication.Models.Blog;

namespace GeekApplication.Interfaces;

public interface IBlogRepository
{
    Task<bool> UserHasPermissionAsync(int userId, string permissionName, CancellationToken ct = default);

    Task<IReadOnlyList<BlogPostFlatDto>> SearchPostsWithOptimizedPlanAsync(
        string searchTerm,
        string languageCode,
        CancellationToken ct = default);

    Task<BlogPostFlatDto?> GetPostBySlugAsync(
        string slug,
        string languageCode,
        CancellationToken ct = default);

    Task<IReadOnlyList<BlogPostFlatDto>> GetTechnicalArticlesOnlyAsync(
        string languageCode,
        CancellationToken ct = default);

    Task<IReadOnlyList<CommentDto>> GetThreadedCommentsAsync(
        int postId,
        CancellationToken ct = default);

    Task<int> InsertCommentReplyWithoutLocalTransactionAsync(
        int postId,
        int? userId,
        string content,
        string? parentPath,
        CancellationToken ct = default);
}
