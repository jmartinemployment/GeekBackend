using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;

namespace GeekRepository.Providers.Seo;

public sealed class WordPressRestProvider(IHttpClientFactory httpClientFactory) : IWordPressProvider
{
    public string ProviderName => "wordpress_rest";

    public async Task<Result<WordPressConnectionTestResult>> TestConnectionAsync(
        WordPressCredentials credentials, CancellationToken ct = default)
    {
        var client = CreateClient(credentials);
        var baseUrl = NormalizeSiteUrl(credentials.SiteUrl);
        var response = await client.GetAsync($"{baseUrl}/wp-json/wp/v2/users/me", ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return Result<WordPressConnectionTestResult>.Failure(
                $"WordPress connection failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : credentials.Username;
        return Result<WordPressConnectionTestResult>.Success(new WordPressConnectionTestResult
        {
            Success = true,
            SiteTitle = name,
        });
    }

    public async Task<Result<WordPressPublishProviderResult>> PublishPostAsync(
        WordPressCredentials credentials, WordPressPostPayload post, CancellationToken ct = default)
    {
        var client = CreateClient(credentials);
        var baseUrl = NormalizeSiteUrl(credentials.SiteUrl);
        var payload = new Dictionary<string, object>
        {
            ["title"] = post.Title,
            ["content"] = post.ContentHtml,
            ["status"] = post.Status,
        };
        if (!string.IsNullOrWhiteSpace(post.Slug))
            payload["slug"] = post.Slug;

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{baseUrl}/wp-json/wp/v2/posts", content, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return Result<WordPressPublishProviderResult>.Failure($"WordPress publish failed ({(int)response.StatusCode}): {raw}");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var postId = root.GetProperty("id").GetInt32();
        var link = root.GetProperty("link").GetString() ?? string.Empty;
        var status = root.GetProperty("status").GetString() ?? post.Status;
        return Result<WordPressPublishProviderResult>.Success(new WordPressPublishProviderResult
        {
            PostId = postId,
            Link = link,
            Status = status,
        });
    }

    private HttpClient CreateClient(WordPressCredentials credentials)
    {
        var client = httpClientFactory.CreateClient("WordPress");
        var password = credentials.ApplicationPassword.Replace(" ", string.Empty, StringComparison.Ordinal);
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{password}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        client.Timeout = TimeSpan.FromSeconds(60);
        return client;
    }

    private static string NormalizeSiteUrl(string siteUrl)
    {
        var url = siteUrl.Trim().TrimEnd('/');
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://{url}";
        }
        return url;
    }
}
