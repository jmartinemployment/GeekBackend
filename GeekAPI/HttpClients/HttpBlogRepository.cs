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

    public Task<bool> UserHasRoleAsync(int userId, string roleName, CancellationToken ct = default) =>
        throw new NotSupportedException("RBAC checks are evaluated on the repository host.");

    public async Task<IReadOnlyList<BlogPostFlatDto>> GetAllPostsAsync(
        string? languageCode = null,
        string? status = null,
        string? postType = null,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (languageCode is not null) query.Add($"lang={Uri.EscapeDataString(languageCode)}");
        if (status is not null) query.Add($"status={Uri.EscapeDataString(status)}");
        if (postType is not null) query.Add($"postType={Uri.EscapeDataString(postType)}");
        var qs = query.Count > 0 ? "?" + string.Join('&', query) : string.Empty;

        var response = await _http.GetAsync($"repo/content/blog/all{qs}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<BlogPostFlatDto>>(ct) ?? [];
    }

    public async Task<BlogPostFlatDto?> GetPostByIdAsync(
        int postId,
        string? languageCode = null,
        CancellationToken ct = default)
    {
        var qs = languageCode is not null ? $"?lang={Uri.EscapeDataString(languageCode)}" : string.Empty;
        var response = await _http.GetAsync($"repo/content/blog/by-id/{postId}{qs}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BlogPostFlatDto>(ct);
    }

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
        var encodedSlug = EncodeSlugPath(slug);
        var response = await _http.GetAsync($"repo/content/blog/{Uri.EscapeDataString(languageCode)}/{encodedSlug}", ct);
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

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(
        string? languageCode = null,
        CancellationToken ct = default)
    {
        var qs = languageCode is not null ? $"?lang={Uri.EscapeDataString(languageCode)}" : string.Empty;
        var response = await _http.GetAsync($"repo/content/blog/categories{qs}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CategoryDto>>(ct) ?? [];
    }

    public async Task<IReadOnlyList<CategoryPostSummaryDto>> GetHomePagePillarsAsync(
        string languageCode = "en", CancellationToken ct = default) =>
        await GetCategorySummaryAsync("home-page-pillars", languageCode, ct);

    public async Task<IReadOnlyList<CategoryPostSummaryDto>> GetPillarSummaryPageAsync(
        string languageCode = "en", CancellationToken ct = default) =>
        await GetCategorySummaryAsync("pillar-summary-page", languageCode, ct);

    public async Task<IReadOnlyList<CategoryPostSummaryDto>> GetToolsSummaryPageAsync(
        string languageCode = "en", CancellationToken ct = default) =>
        await GetCategorySummaryAsync("tools-summary-page", languageCode, ct);

    public async Task<IReadOnlyList<CategoryPostSummaryDto>> GetBlogSummaryPageAsync(
        string languageCode = "en", CancellationToken ct = default) =>
        await GetCategorySummaryAsync("blog-summary-page", languageCode, ct);

    private async Task<IReadOnlyList<CategoryPostSummaryDto>> GetCategorySummaryAsync(
        string route, string languageCode, CancellationToken ct)
    {
        var response = await _http.GetAsync(
            $"repo/content/blog/{route}?lang={Uri.EscapeDataString(languageCode)}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CategoryPostSummaryDto>>(ct) ?? [];
    }

    public async Task<int> CreatePostAsync(UpsertBlogPostCommand command, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("repo/content/blog", command, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"CMS create post failed ({(int)response.StatusCode} {response.ReasonPhrase}): {TruncateBody(body)}");
        }

        var created = await response.Content.ReadFromJsonAsync<BlogPostFlatDto>(ct);
        return created?.PostId ?? 0;
    }

    public async Task<bool> UpdatePostAsync(int postId, UpsertBlogPostCommand command, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"repo/content/blog/{postId}", command, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"CMS update post failed ({(int)response.StatusCode} {response.ReasonPhrase}): {TruncateBody(body)}");
        }

        return true;
    }

    public async Task<bool> DeletePostAsync(int postId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"repo/content/blog/{postId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
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
        string? attachmentUrl,
        CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"repo/content/blog/{postId}/comments",
            new { userId, content, parentPath, attachmentUrl },
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

    private static string EncodeSlugPath(string slug) =>
        string.Join('/', slug.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private static string TruncateBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(empty body)";
        var trimmed = body.Trim();
        return trimmed.Length <= 800 ? trimmed : trimmed[..800] + "…";
    }
}

