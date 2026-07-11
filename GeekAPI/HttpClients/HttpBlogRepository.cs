using System.Net;
using System.Net.Http.Json;
using GeekApplication.Interfaces;
using GeekApplication.Models.Blog;

namespace GeekAPI.HttpClients;

public sealed class HttpBlogRepository : IBlogRepository
{
    private readonly HttpClient _http;

    public HttpBlogRepository(IHttpClientFactory factory) =>
        _http = factory.CreateClient("GeekRepository");

    public Task<bool> UserHasPermissionAsync(int userId, string permissionName, CancellationToken ct = default) =>
        throw new NotSupportedException("RBAC checks are evaluated on the repository host.");

    public async Task<IReadOnlyList<BlogPostFlatDto>> SearchPostsWithOptimizedPlanAsync(
        string searchTerm,
        string languageCode,
        CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"repo/content/blog/search?q={Uri.EscapeDataString(searchTerm)}&lang={Uri.EscapeDataString(languageCode)}",
            ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<BlogPostFlatDto>>(ct) ?? [];
    }

    public async Task<BlogPostFlatDto?> GetPostBySlugAsync(
        string slug,
        string languageCode,
        CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"repo/content/blog/{Uri.EscapeDataString(languageCode)}/{Uri.EscapeDataString(slug)}",
            ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BlogPostFlatDto>(ct);
    }

    public async Task<IReadOnlyList<BlogPostFlatDto>> GetTechnicalArticlesOnlyAsync(
        string languageCode,
        CancellationToken ct = default)
    {
        var response = await _http.GetAsync(
            $"repo/content/blog/technical/{Uri.EscapeDataString(languageCode)}",
            ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<BlogPostFlatDto>>(ct) ?? [];
    }

    public async Task<IReadOnlyList<CommentDto>> GetThreadedCommentsAsync(
        int postId,
        CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"repo/content/blog/{postId}/comments", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CommentDto>>(ct) ?? [];
    }

    public async Task<int> InsertCommentReplyWithoutLocalTransactionAsync(
        int postId,
        int? userId,
        string content,
        string? parentPath,
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"repo/content/blog/{postId}/comments",
            new { userId, content, parentPath },
            ct);
        response.EnsureSuccessStatusCode();

        if (response.Headers.Location is not null)
        {
            var created = await response.Content.ReadFromJsonAsync<CommentDto>(ct);
            if (created is not null) return created.Id;
        }

        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>(ct);
        return payload?["id"] ?? 0;
    }
}
