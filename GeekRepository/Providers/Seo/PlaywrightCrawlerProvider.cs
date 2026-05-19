using System.Text.Json;
using System.Text.RegularExpressions;
using GeekApplication.Interfaces.Seo;
using GeekApplication.Models.Seo;
using GeekApplication.Results;
using Microsoft.Playwright;

namespace GeekRepository.Providers.Seo;

public sealed class PlaywrightCrawlerProvider(IBrowser browser) : ICrawlerProvider
{
    private static readonly SemaphoreSlim CrawlSemaphore = new(2, 2);
    private static readonly HttpClient RobotsClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    public string ProviderName => "playwright";

    public async Task<Result<PageContent>> CrawlPageAsync(string url, CancellationToken ct = default)
    {
        if (!await IsAllowedByRobotsTxtAsync(url, ct))
            return Result<PageContent>.Failure($"URL {url} disallowed by robots.txt");

        await CrawlSemaphore.WaitAsync(ct);
        try
        {
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            await page.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
            {
                { "User-Agent", "GeekSEO-Bot/1.0 (content analysis; contact: jeff@geekatyourspot.com)" },
            });

            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                Timeout = 30_000,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            });

            var httpStatus = response?.Status ?? 0;
            if (httpStatus >= 400)
                return Result<PageContent>.Failure($"HTTP {httpStatus} for {url}");

            var metaTitle = await page.TitleAsync();
            var bodyText = await page.EvalOnSelectorAsync<string>("body", "el => el ? el.innerText : ''") ?? string.Empty;
            var metaDescription = await page.GetAttributeAsync("meta[name=\"description\"]", "content");
            var headings = await ExtractHeadingsAsync(page);
            var structuredTypes = await ExtractStructuredDataTypesAsync(page);
            var wordCount = CountWords(bodyText);

            return Result<PageContent>.Success(new PageContent
            {
                Url = url,
                FullText = bodyText,
                MetaTitle = metaTitle,
                MetaDescription = metaDescription,
                WordCount = wordCount,
                HttpStatusCode = httpStatus,
                Headings = headings,
                HasStructuredData = structuredTypes.Count > 0,
                StructuredDataTypes = structuredTypes,
                CrawledAt = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            return Result<PageContent>.Failure($"Crawl failed for {url}: {ex.Message}");
        }
        finally
        {
            CrawlSemaphore.Release();
        }
    }

    public async Task<bool> IsAllowedByRobotsTxtAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        try
        {
            var robotsUrl = $"{uri.Scheme}://{uri.Host}/robots.txt";
            using var response = await RobotsClient.GetAsync(robotsUrl, ct);
            if (!response.IsSuccessStatusCode)
                return true;

            var text = await response.Content.ReadAsStringAsync(ct);
            return !IsPathDisallowed(text, uri.AbsolutePath);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsPathDisallowed(string robotsTxt, string path)
    {
        var lines = robotsTxt.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var applies = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('#'))
                continue;

            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
            {
                var agent = line["User-agent:".Length..].Trim();
                applies = agent is "*" or "GeekSEO-Bot";
                continue;
            }

            if (applies && line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
            {
                var disallow = line["Disallow:".Length..].Trim();
                if (disallow.Length == 0)
                    continue;
                if (path.StartsWith(disallow, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static async Task<IReadOnlyList<PageHeading>> ExtractHeadingsAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>(@"() => {
            return JSON.stringify(
                Array.from(document.querySelectorAll('h1,h2,h3,h4,h5,h6'))
                    .map(el => ({ level: Number(el.tagName.substring(1)), text: el.innerText.trim() }))
                    .filter(h => h.text.length > 0)
            );
        }");

        return JsonSerializer.Deserialize<List<PageHeading>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    private static async Task<IReadOnlyList<string>> ExtractStructuredDataTypesAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>(@"() => {
            const types = new Set();
            document.querySelectorAll('script[type=""application/ld+json""]').forEach(node => {
                try {
                    const data = JSON.parse(node.textContent || '{}');
                    const collect = (obj) => {
                        if (!obj || typeof obj !== 'object') return;
                        if (obj['@type']) types.add(String(obj['@type']));
                        Object.values(obj).forEach(collect);
                    };
                    collect(data);
                } catch {}
            });
            return JSON.stringify(Array.from(types));
        }");

        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : Regex.Split(text.Trim(), @"\s+").Length;
}
