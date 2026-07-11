using System.Net.Http.Json;
using System.Text.Json;

namespace ImportBlogContent;

public sealed class BlogImporter(HttpClient http, string apiKey)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ImportPreflight> PreflightAsync(
        IReadOnlyList<ImportManifestEntry> manifest,
        CancellationToken ct = default)
    {
        var existing = await LoadExistingBySlugAsync(ct);
        var preflight = new ImportPreflight();

        foreach (var entry in manifest)
        {
            var key = SlugKey(entry.Slug, entry.LanguageCode);
            if (existing.ContainsKey(key))
                preflight.Existing++;
            else
                preflight.New++;
        }

        return preflight;
    }

    public async Task<ImportResult> ImportAsync(
        IReadOnlyList<ImportManifestEntry> manifest,
        bool dryRun,
        bool replace,
        CancellationToken ct = default)
    {
        var result = new ImportResult();
        var existing = await LoadExistingBySlugAsync(ct);

        foreach (var entry in manifest)
        {
            try
            {
                var key = SlugKey(entry.Slug, entry.LanguageCode);
                existing.TryGetValue(key, out var existingId);
                var hasExisting = existing.ContainsKey(key);

                if (dryRun)
                {
                    result.DryRun++;
                    var action = hasExisting ? (replace ? "update" : "skip") : "create";
                    Console.WriteLine($"[dry-run] {action} {entry.PostType} {entry.Slug}");
                    continue;
                }

                if (hasExisting && !replace)
                {
                    result.SkippedExisting++;
                    Console.WriteLine($"[skip-existing] {entry.Slug} (use --replace to update)");
                    continue;
                }

                var html = await http.GetStringAsync(entry.Url, ct);
                var parsed = PageParser.Parse(html, entry.PostType, entry.Url);

                var payload = new
                {
                    postType = entry.PostType,
                    status = "published",
                    languageCode = entry.LanguageCode,
                    slug = entry.Slug,
                    title = parsed.Title,
                    body = parsed.BodyHtml,
                    schemaMetadataJson = parsed.SchemaMetadataJson,
                    tagSlugs = new[] { entry.Department },
                    publishedAt = parsed.PublishedAt?.ToUniversalTime()
                };

                using var request = new HttpRequestMessage(
                    hasExisting ? HttpMethod.Put : HttpMethod.Post,
                    hasExisting
                        ? $"api/blog/{existingId}"
                        : "api/blog")
                {
                    Content = JsonContent.Create(payload, options: JsonOptions)
                };
                request.Headers.Add("X-API-Key", apiKey);

                var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    result.Errors.Add($"{entry.Slug}: {(int)response.StatusCode} {body}");
                    continue;
                }

                if (hasExisting)
                {
                    result.Updated++;
                    Console.WriteLine($"[updated] {entry.Slug}");
                }
                else
                {
                    result.Created++;
                    Console.WriteLine($"[created] {entry.Slug}");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{entry.Slug}: {ex.Message}");
            }
        }

        return result;
    }

    private async Task<Dictionary<string, int>> LoadExistingBySlugAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/blog/all");
        request.Headers.Add("X-API-Key", apiKey);
        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to list existing posts: {(int)response.StatusCode}");

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("slug", out var s)
                || !item.TryGetProperty("languageCode", out var l)
                || !item.TryGetProperty("postId", out var id))
                continue;

            var slug = s.GetString();
            var lang = l.GetString();
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(lang)) continue;

            map[SlugKey(slug, lang)] = id.GetInt32();
        }

        return map;
    }

    private static string SlugKey(string slug, string lang) => $"{lang}:{slug}";
}

public sealed class ImportPreflight
{
    public int New { get; set; }
    public int Existing { get; set; }
    public int Total => New + Existing;
}

public sealed class ImportResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int SkippedExisting { get; set; }
    public int DryRun { get; set; }
    public List<string> Errors { get; } = [];

    public int Imported => Created + Updated;
}
