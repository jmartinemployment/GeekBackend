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

            if (!UseCasePostNormalizer.NeedsNormalization(post.Slug, post.Title, post.JsonLdOverride ?? "{}"))
            {
                result.Skipped++;
                continue;
            }

            var (displayTitle, schema) = UseCasePostNormalizer.Normalize(
                post.Slug,
                post.Title,
                post.JsonLdOverride ?? "{}");

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
                schemaType = post.SchemaType,
                isPublished = post.IsPublished,
                languageCode = post.LanguageCode,
                slug = post.Slug,
                title = displayTitle,
                summary = post.Summary,
                metaDescription = post.MetaDescription,
                jsonLdOverride = schema,
                sections = post.Sections,
                tagSlugs,
                categorySlug = post.CategorySlug,
                presentation = post.Presentation,
                cwJobId = post.CwJobId,
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
        public string SchemaType { get; init; } = string.Empty;
        public bool IsPublished { get; init; }
        public string LanguageCode { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string? MetaDescription { get; init; }
        public string? JsonLdOverride { get; init; }
        public IReadOnlyList<AdminPostSection> Sections { get; init; } = [];
        public string LocalizedTagsJson { get; init; } = "[]";
        public string CategorySlug { get; init; } = string.Empty;
        public Dictionary<string, string> Presentation { get; init; } = new();
        public string? CwJobId { get; init; }
        public DateTimeOffset? PublishedAt { get; init; }
    }

    private sealed class AdminPostSection
    {
        public int SortOrder { get; init; }
        public string? HeadingTag { get; init; }
        public string? HeadingText { get; init; }
        public string BodyContent { get; init; } = string.Empty;
        public string? MediaUrl { get; init; }
        public string? MediaAlt { get; init; }
    }
}

public sealed class UseCaseTitleMigrationResult
{
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int DryRun { get; set; }
    public List<string> Errors { get; } = [];
}
