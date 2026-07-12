using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeekApplication.Blog;

namespace ImportBlogContent;

public sealed class UseCaseTitleMigrator(HttpClient http, string apiKey)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<UseCaseTitleMigrationResult> MigrateAsync(bool dryRun, CancellationToken ct = default)
    {
        var result = new UseCaseTitleMigrationResult();
        var posts = await LoadPostsAsync(ct);

        foreach (var post in posts)
        {
            if (!UseCasePostNormalizer.IsUseCaseSlug(post.Slug))
            {
                result.Skipped++;
                continue;
            }

            if (!UseCasePostNormalizer.NeedsNormalization(post.Slug, post.Title, post.SchemaMetadataJson))
            {
                result.Skipped++;
                continue;
            }

            var (displayTitle, schema) = UseCasePostNormalizer.Normalize(
                post.Slug,
                post.Title,
                post.SchemaMetadataJson);

            if (dryRun)
            {
                result.DryRun++;
                Console.WriteLine($"[dry-run] fix-title {post.Slug}");
                Console.WriteLine($"  was: {post.Title}");
                Console.WriteLine($"  now: {displayTitle}");
                continue;
            }

            var tagSlugs = ParseTagSlugs(post.LocalizedTagsJson);
            var payload = new
            {
                postType = post.PostType,
                status = post.Status,
                languageCode = post.LanguageCode,
                slug = post.Slug,
                title = displayTitle,
                body = post.Body,
                schemaMetadataJson = schema,
                tagSlugs,
                publishedAt = post.PublishedAt?.ToUniversalTime(),
            };

            using var request = new HttpRequestMessage(HttpMethod.Put, $"api/blog/{post.PostId}")
            {
                Content = JsonContent.Create(payload, options: JsonOptions),
            };
            request.Headers.Add("X-API-Key", apiKey);

            var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                result.Errors.Add($"{post.Slug}: {(int)response.StatusCode} {body}");
                continue;
            }

            result.Updated++;
            Console.WriteLine($"[title] {post.Slug} -> {displayTitle}");
        }

        return result;
    }

    private async Task<IReadOnlyList<AdminPost>> LoadPostsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/blog/all");
        request.Headers.Add("X-API-Key", apiKey);
        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var posts = await response.Content.ReadFromJsonAsync<List<AdminPost>>(JsonOptions, ct);
        return posts ?? [];
    }

    private static IReadOnlyList<string> ParseTagSlugs(string localizedTagsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(localizedTagsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            return doc.RootElement.EnumerateArray()
                .Select(tag => tag.TryGetProperty("slug", out var slug) ? slug.GetString() : null)
                .Where(slug => !string.IsNullOrWhiteSpace(slug))
                .Select(slug => slug!)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private sealed class AdminPost
    {
        public int PostId { get; init; }
        public string PostType { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string LanguageCode { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string SchemaMetadataJson { get; init; } = "{}";
        public string LocalizedTagsJson { get; init; } = "[]";
        public DateTimeOffset? PublishedAt { get; init; }
    }
}

public sealed class UseCaseTitleMigrationResult
{
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int DryRun { get; set; }
    public List<string> Errors { get; } = [];
}
