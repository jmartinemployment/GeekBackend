using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ImportBlogContent;

public sealed record JsonLdBuildInput(
    string PostType,
    string Slug,
    string Title,
    string Body,
    string? Description,
    DateTimeOffset? PublishedAt,
    string? RelatedCitationUrl);

public static partial class JsonLdSchemaBuilder
{
    private const string SiteBaseUrl = "https://www.geekatyourspot.com";
    private const string PublisherName = "Geek At Your Spot";
    private const string PublisherLogoUrl = "https://seo.geekatyourspot.com/logo.png";
    private const string AuthorName = "Geek At Your Spot Editorial Team";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Build(JsonLdBuildInput input)
    {
        var canonicalUrl = $"{SiteBaseUrl}/{input.Slug.TrimStart('/')}";
        var publishedAt = input.PublishedAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var description = input.Description?.Trim();
        if (string.IsNullOrWhiteSpace(description))
            description = ExtractLeadDescription(input.Body);

        var wordCount = CountWords(input.Body);
        var keywords = BuildKeywords(input.Slug, input.Title, description);

        if (string.Equals(input.PostType, "BlogPosting", StringComparison.Ordinal))
        {
            return SerializeBlogPosting(input.Title, description, canonicalUrl, publishedAt, wordCount, keywords, input.RelatedCitationUrl);
        }

        var softwareApps = ExtractSoftwareApplications(input.Body);
        return SerializeTechnicalArticle(
            input.Title,
            description,
            canonicalUrl,
            publishedAt,
            wordCount,
            keywords,
            input.RelatedCitationUrl,
            softwareApps);
    }

    public static string? FindRelatedCitationUrl(string body, string postType, string slug)
    {
        var links = ExtractInternalLinks(body).ToList();
        if (links.Count == 0)
            return null;

        if (string.Equals(postType, "BlogPosting", StringComparison.Ordinal))
        {
            return links.FirstOrDefault(link =>
                link.Contains("/use-cases/", StringComparison.OrdinalIgnoreCase)
                || link.Contains("/tools/", StringComparison.OrdinalIgnoreCase));
        }

        return links.FirstOrDefault(link => link.Contains("/blog/", StringComparison.OrdinalIgnoreCase));
    }

    private static string SerializeBlogPosting(
        string title,
        string? description,
        string canonicalUrl,
        DateTimeOffset publishedAt,
        int wordCount,
        string keywords,
        string? relatedCitationUrl)
    {
        var schema = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["headline"] = title,
            ["description"] = description,
            ["image"] = new[] { PublisherLogoUrl },
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = AuthorName,
            },
            ["publisher"] = BuildPublisher(),
            ["datePublished"] = publishedAt.ToString("O"),
            ["dateModified"] = publishedAt.ToString("O"),
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = canonicalUrl,
            },
            ["keywords"] = keywords,
            ["wordCount"] = wordCount,
        };

        if (!string.IsNullOrWhiteSpace(relatedCitationUrl))
        {
            schema["citation"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["@type"] = "TechnicalArticle",
                    ["url"] = relatedCitationUrl,
                },
            };
        }

        return JsonSerializer.Serialize(schema, JsonOptions);
    }

    private static string SerializeTechnicalArticle(
        string title,
        string? description,
        string canonicalUrl,
        DateTimeOffset publishedAt,
        int wordCount,
        string keywords,
        string? relatedCitationUrl,
        IReadOnlyList<SoftwareApplicationEntry> softwareApps)
    {
        var articleNode = new Dictionary<string, object?>
        {
            ["@type"] = "TechnicalArticle",
            ["headline"] = title,
            ["description"] = description,
            ["image"] = new[] { PublisherLogoUrl },
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = AuthorName,
            },
            ["publisher"] = BuildPublisher(),
            ["datePublished"] = publishedAt.ToString("O"),
            ["dateModified"] = publishedAt.ToString("O"),
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = canonicalUrl,
            },
            ["keywords"] = keywords,
            ["wordCount"] = wordCount,
            ["proficiencyLevel"] = "Beginner",
        };

        if (!string.IsNullOrWhiteSpace(relatedCitationUrl))
        {
            articleNode["citation"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["@type"] = "BlogPosting",
                    ["url"] = relatedCitationUrl,
                },
            };
        }

        if (softwareApps.Count == 0)
        {
            var single = new Dictionary<string, object?>(articleNode)
            {
                ["@context"] = "https://schema.org",
            };
            return JsonSerializer.Serialize(single, JsonOptions);
        }

        var graph = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new List<Dictionary<string, object?>>(
                [articleNode, ..softwareApps.Select(ToSoftwareApplicationNode)]),
        };

        return JsonSerializer.Serialize(graph, JsonOptions);
    }

    private static Dictionary<string, object?> BuildPublisher() =>
        new()
        {
            ["@type"] = "Organization",
            ["name"] = PublisherName,
            ["logo"] = new Dictionary<string, object?>
            {
                ["@type"] = "ImageObject",
                ["url"] = PublisherLogoUrl,
            },
        };

    private static Dictionary<string, object?> ToSoftwareApplicationNode(SoftwareApplicationEntry app) =>
        new()
        {
            ["@type"] = "SoftwareApplication",
            ["name"] = app.Name,
            ["applicationCategory"] = "BusinessApplication",
            ["operatingSystem"] = "Web",
            ["description"] = app.Description,
        };

    private static IReadOnlyList<SoftwareApplicationEntry> ExtractSoftwareApplications(string body)
    {
        var lines = body.Split('\n');
        var inToolsSection = false;
        var apps = new List<SoftwareApplicationEntry>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inToolsSection = TopToolsSectionRegex().IsMatch(line);
                continue;
            }

            if (!inToolsSection || !line.StartsWith("### ", StringComparison.Ordinal))
                continue;

            var name = line[4..].Trim();
            if (string.IsNullOrWhiteSpace(name) || ToolHeadingSkipRegex().IsMatch(name))
                continue;

            var description = ReadParagraph(lines, i + 1);
            if (string.IsNullOrWhiteSpace(description))
                continue;

            apps.Add(new SoftwareApplicationEntry(name, description));
        }

        return apps;
    }

    private static string ReadParagraph(string[] lines, int startIndex)
    {
        var parts = new List<string>();
        for (var i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('#'))
                break;
            if (line.Length == 0)
            {
                if (parts.Count > 0)
                    break;
                continue;
            }

            if (line.StartsWith('-') || line.StartsWith('*') || line.StartsWith("**How", StringComparison.Ordinal))
                break;

            parts.Add(line);
        }

        return string.Join(' ', parts).Trim();
    }

    private static IEnumerable<string> ExtractInternalLinks(string body)
    {
        foreach (Match match in MarkdownLinkRegex().Matches(body))
        {
            var href = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(href))
                continue;

            if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (href.Contains("geekatyourspot.com", StringComparison.OrdinalIgnoreCase))
                    yield return href.Split('#')[0].TrimEnd('/');
                continue;
            }

            if (href.StartsWith('/'))
                yield return $"{SiteBaseUrl}{href.Split('#')[0].TrimEnd('/')}";
        }
    }

    private static string? ExtractLeadDescription(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            if (trimmed.StartsWith('-') || trimmed.StartsWith('*'))
                continue;
            return trimmed;
        }

        return null;
    }

    private static int CountWords(string body) =>
        WordRegex().Matches(body).Count;

    private static string BuildKeywords(string slug, string title, string? description)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in slug.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part is "blog" or "use-cases" or "tools")
                continue;
            tokens.Add(part.Replace('-', ' '));
        }

        foreach (Match match in WordRegex().Matches(title))
            tokens.Add(match.Value);

        if (!string.IsNullOrWhiteSpace(description))
        {
            foreach (Match match in WordRegex().Matches(description))
            {
                if (match.Value.Length >= 4)
                    tokens.Add(match.Value);
            }
        }

        return string.Join(", ", tokens.Take(12));
    }

    private sealed record SoftwareApplicationEntry(string Name, string Description);

    [GeneratedRegex(@"\]\(([^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"\b[\w']+\b", RegexOptions.Compiled)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"^##\s+Top\b.*\bTools\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TopToolsSectionRegex();

    [GeneratedRegex(@"^(?:\d+\.|Establishing|Utilizing|Conducting|Understanding|Identifying|Implementing|Benefits|Core|What\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ToolHeadingSkipRegex();
}
