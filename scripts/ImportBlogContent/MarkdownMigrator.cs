using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImportBlogContent;

public sealed class MarkdownMigrator(HttpClient http, string apiKey)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<MarkdownMigrationResult> MigrateAsync(bool dryRun, CancellationToken ct = default)
    {
        var result = new MarkdownMigrationResult();
        var posts = await LoadPostsAsync(ct);

        foreach (var post in posts)
        {
            if (!HtmlToMarkdown.LooksLikeHtmlForMigration(post.Body))
            {
                result.Skipped++;
                continue;
            }

            var markdown = HtmlToMarkdown.Convert(post.Body);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                result.Errors.Add($"{post.Slug}: conversion produced empty body");
                continue;
            }

            if (dryRun)
            {
                result.DryRun++;
                Console.WriteLine($"[dry-run] convert {post.PostId} {post.Slug} ({post.Body.Length} -> {markdown.Length} chars)");
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
                body = markdown,
                schemaMetadataJson = post.SchemaMetadataJson,
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
            Console.WriteLine($"[converted] {post.Slug}");
        }

        return result;
    }

    private async Task<List<AdminPost>> LoadPostsAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/blog/all");
        request.Headers.Add("X-API-Key", apiKey);
        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to list posts: {(int)response.StatusCode}");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<List<AdminPost>>(stream, JsonOptions, ct) ?? [];
    }

    private static List<string> ParseTagSlugs(string localizedTagsJson)
    {
        if (string.IsNullOrWhiteSpace(localizedTagsJson))
            return [];

        try
        {
            using var json = JsonDocument.Parse(localizedTagsJson);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var tags = new List<string>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var slug = item.GetString();
                    if (!string.IsNullOrWhiteSpace(slug))
                        tags.Add(slug);
                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("slug", out var slugElement))
                {
                    var slug = slugElement.GetString();
                    if (!string.IsNullOrWhiteSpace(slug))
                        tags.Add(slug);
                }
            }

            return tags;
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
        public string LanguageCode { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset? PublishedAt { get; init; }
        public string LocalizedTagsJson { get; init; } = "[]";
        public string SchemaMetadataJson { get; init; } = "{}";
    }
}

public sealed class MarkdownMigrationResult
{
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int DryRun { get; set; }
    public List<string> Errors { get; } = [];
}
