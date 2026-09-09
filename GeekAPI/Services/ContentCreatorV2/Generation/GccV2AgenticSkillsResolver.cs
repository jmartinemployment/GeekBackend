using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

public sealed record GccV2AgenticSkillsImportRequest(
    string ListingUrl,
    string SemanticVersion = "1.0.0",
    IReadOnlyList<string>? Stages = null,
    IReadOnlyList<string>? ContentTypes = null,
    IReadOnlyList<string>? RequiredTools = null,
    int Order = 100,
    string ActivationMode = "automatic");

public sealed record GccV2ResolvedAgenticSkill(
    string ListingUrl,
    string PackageSpecifier,
    GccV2GitHubSkillImportRequest ImportRequest);

public sealed partial class GccV2AgenticSkillsResolver(IHttpClientFactory clients)
{
    private const int MaximumListingBytes = 1024 * 1024;

    public async Task<GccV2ResolvedAgenticSkill> ResolveAsync(
        GccV2AgenticSkillsImportRequest request,
        CancellationToken ct)
    {
        var listing = ValidateListingUrl(request.ListingUrl);
        var http = clients.CreateClient(nameof(GccV2AgenticSkillsResolver));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var page = await DownloadListingAsync(http, listing, timeout.Token);
        var text = WebUtility.HtmlDecode(HtmlTag().Replace(page, " "));
        var packageMatch = InstallCommand().Match(text);
        if (!packageMatch.Success)
            throw new GccV2SkillImportException(
                "The Agentic Skills listing does not expose a supported `npx skills add owner/repo@skill` source.");

        var owner = packageMatch.Groups["owner"].Value;
        var repository = packageMatch.Groups["repo"].Value;
        var skill = packageMatch.Groups["skill"].Success
            ? packageMatch.Groups["skill"].Value
            : listing.Segments[^1].Trim('/');
        var skillPath = $"skills/{skill}";
        var commit = await ResolveCommitAsync(http, owner, repository, skillPath, timeout.Token);
        var packageSpecifier = $"{owner}/{repository}@{skill}";
        return new GccV2ResolvedAgenticSkill(
            listing.AbsoluteUri,
            packageSpecifier,
            new(
                $"https://github.com/{owner}/{repository}",
                commit,
                skillPath,
                request.SemanticVersion,
                request.Stages,
                request.ContentTypes,
                request.RequiredTools,
                request.Order,
                request.ActivationMode));
    }

    private static Uri ValidateListingUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("agenticskills.io", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/skills/", StringComparison.Ordinal)
            || uri.AbsolutePath.Trim('/').Split('/').Length != 2)
            throw new GccV2SkillImportException(
                "listingUrl must be an https://agenticskills.io/skills/{skill} page.");
        return uri;
    }

    private static async Task<string> DownloadListingAsync(
        HttpClient http, Uri listing, CancellationToken ct)
    {
        using var response = await http.GetAsync(
            listing, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumListingBytes)
            throw new GccV2SkillImportException("Agentic Skills listing exceeds the size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (memory.Length + read > MaximumListingBytes)
                throw new GccV2SkillImportException("Agentic Skills listing exceeds the size limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return new UTF8Encoding(false, true).GetString(memory.ToArray());
    }

    private static async Task<string> ResolveCommitAsync(
        HttpClient http,
        string owner,
        string repository,
        string skillPath,
        CancellationToken ct)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{owner}/{repository}/commits"
            + $"?path={Uri.EscapeDataString(skillPath + "/SKILL.md")}&per_page=1");
        message.Headers.UserAgent.ParseAdd("ContentCreatorV2-SkillImporter/1.0");
        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() == 0
            || !document.RootElement[0].TryGetProperty("sha", out var sha)
            || sha.GetString() is not { Length: 40 } commit
            || !commit.All(Uri.IsHexDigit))
            throw new GccV2SkillImportException(
                "The upstream GitHub skill path has no immutable commit.");
        return commit.ToLowerInvariant();
    }

    [GeneratedRegex(
        @"npx\s+skills\s+add\s+(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)(?:@(?<skill>[A-Za-z0-9_.-]+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallCommand();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();
}
