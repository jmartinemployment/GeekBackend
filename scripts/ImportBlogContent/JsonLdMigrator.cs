using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImportBlogContent;

public sealed class JsonLdMigrator(HttpClient http, string apiKey)
{
    private const string SiteBaseUrl = "https://www.geekatyourspot.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<JsonLdMigrationResult> MigrateAsync(bool dryRun, CancellationToken ct = default)
    {
        var result = new JsonLdMigrationResult();
        var posts = await LoadPostsAsync(ct);
        var citationBySlug = BuildCitationMap(posts);

        foreach (var post in posts)
        {
            var relatedUrl = JsonLdSchemaBuilder.FindRelatedCitationUrl(post.Body, post.PostType, post.Slug)
                ?? citationBySlug.GetValueOrDefault(post.Slug);
            var description = TryReadDescription(post.SchemaMetadataJson);
            var rebuilt = JsonLdSchemaBuilder.Build(new JsonLdBuildInput(
                post.PostType,
                post.Slug,
                post.Title,
                post.Body,
                description,
                post.PublishedAt,
                relatedUrl));

            if (SchemasEquivalent(post.SchemaMetadataJson, rebuilt))
            {
                result.Skipped++;
                continue;
            }

            if (dryRun)
            {
                result.DryRun++;
                Console.WriteLine(
                    $"[dry-run] restore-jsonld {post.Slug} ({post.SchemaMetadataJson.Length} -> {rebuilt.Length} chars)");
                continue;
            }

            var tagSlugs = ParseTagSlugs(post.LocalizedTagsJson);
            var payload = new
            {
                postType = post.PostType,
                status = post.Status,
                languageCode = post.LanguageCode,
                slug = post.Slug,
                title = post.Title,
                body = post.Body,
                schemaMetadataJson = rebuilt,
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
            Console.WriteLine($"[jsonld] {post.Slug}");
        }

        return result;
    }

    private static Dictionary<string, string> BuildCitationMap(IReadOnlyList<AdminPost> posts)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var post in posts)
        {
            if (!string.Equals(post.PostType, "BlogPosting", StringComparison.Ordinal))
                continue;

            var pillarUrl = JsonLdSchemaBuilder.FindRelatedCitationUrl(post.Body, post.PostType, post.Slug);
            if (string.IsNullOrWhiteSpace(pillarUrl))
                continue;

            var pillarSlug = UrlToApiSlug(pillarUrl);
            if (pillarSlug is null)
                continue;

            var blogUrl = $"{SiteBaseUrl}/{post.Slug.TrimStart('/')}";
            map.TryAdd(pillarSlug, blogUrl);
        }

        return map;
    }

    private static string? UrlToApiSlug(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var path = uri.AbsolutePath.Trim('/');
        if (path.Length == 0)
            return null;

        return path;
    }

    private static bool SchemasEquivalent(string current, string rebuilt)
    {
        try
        {
            using var left = JsonDocument.Parse(current);
            using var right = JsonDocument.Parse(rebuilt);
            return JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryReadDescription(string schemaMetadataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(schemaMetadataJson);
            if (doc.RootElement.TryGetProperty("description", out var description))
                return description.GetString();
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private async Task<List<AdminPost>> LoadPostsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/blog/all");
        request.Headers.Add("X-API-Key", apiKey);
        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to list posts: {(int)response.StatusCode}");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var posts = await JsonSerializer.DeserializeAsync<List<AdminPost>>(stream, JsonOptions, ct) ?? [];
        return posts;
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
        public string LanguageCode { get; init; } = "en";
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string Status { get; init; } = "published";
        public string SchemaMetadataJson { get; init; } = "{}";
        public string LocalizedTagsJson { get; init; } = "[]";
        public DateTimeOffset? PublishedAt { get; init; }
    }
}

public sealed class JsonLdMigrationResult
{
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int DryRun { get; set; }
    public List<string> Errors { get; } = [];
}
