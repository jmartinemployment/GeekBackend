using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.ContentCreatorV2.Context;

namespace GeekAPI.Services.ContentCreatorV2.SharePoint;

public sealed record GccV2SharePointTokenResponse(string AccessToken, string? RefreshToken);

public sealed record GccV2SharePointFileContent(
    string ItemId,
    string Name,
    string MimeType,
    string MediaType,
    string FileName,
    byte[] Bytes,
    DateTimeOffset? ModifiedAtUtc,
    string WebUrl);

/// <summary>Microsoft Graph token + driveItem fetch for approved Knowledge media types.</summary>
public sealed class GccV2SharePointGraphClient(IHttpClientFactory httpClientFactory)
{
    private static readonly Regex ItemIdPattern = new(
        @"^[a-zA-Z0-9!._-]{10,256}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> DownloadableMime = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/markdown",
        "text/csv",
        "text/html",
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "application/msword",
    };

    public static bool TryNormalizeItemRef(string? itemIdOrUrl, out string itemRef, out bool isShareUrl)
    {
        itemRef = "";
        isShareUrl = false;
        var raw = (itemIdOrUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return false;

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return false;
            if (uri.Host.Contains("sharepoint.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Contains("onedrive.live.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Contains("1drv.ms", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Contains("sharepoint-df.com", StringComparison.OrdinalIgnoreCase))
            {
                itemRef = raw;
                isShareUrl = true;
                return true;
            }

            return false;
        }

        if (!ItemIdPattern.IsMatch(raw)) return false;
        itemRef = raw;
        return true;
    }

    public static string ToShareId(string sharingUrl)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(sharingUrl))
            .TrimEnd('=')
            .Replace('/', '_')
            .Replace('+', '-');
        return "u!" + base64;
    }

    public async Task<GccV2SharePointTokenResponse> ExchangeAuthorizationCodeAsync(
        string code, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = GccV2SharePointOAuthEnv.ClientId,
            ["client_secret"] = GccV2SharePointOAuthEnv.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = GccV2SharePointOAuthEnv.RedirectUri,
            ["grant_type"] = "authorization_code",
        };
        return await TokenRequestAsync(form, ct);
    }

    public async Task<string> ExchangeRefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = GccV2SharePointOAuthEnv.ClientId,
            ["client_secret"] = GccV2SharePointOAuthEnv.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["scope"] = string.Join(' ', Scopes),
        };
        var token = await TokenRequestAsync(form, ct);
        return token.AccessToken;
    }

    public static readonly string[] Scopes =
    [
        "offline_access",
        "User.Read",
        "Files.Read.All",
        "Sites.Read.All",
    ];

    public async Task<(string Email, string DisplayName)> GetUserInfoAsync(
        string accessToken, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("GccV2MicrosoftGraph");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var email = doc.RootElement.TryGetProperty("mail", out var mailEl)
            ? mailEl.GetString()?.Trim()
            : null;
        if (string.IsNullOrWhiteSpace(email)
            && doc.RootElement.TryGetProperty("userPrincipalName", out var upnEl))
        {
            email = upnEl.GetString()?.Trim();
        }

        var name = doc.RootElement.TryGetProperty("displayName", out var nameEl)
            ? nameEl.GetString()?.Trim() ?? ""
            : "";
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Microsoft Graph /me did not return an email.");
        return (email, name);
    }

    public async Task<GccV2SharePointFileContent> FetchFileAsync(
        string accessToken, string itemIdOrUrl, CancellationToken ct)
    {
        if (!TryNormalizeItemRef(itemIdOrUrl, out var itemRef, out var isShareUrl))
            throw new InvalidOperationException("SharePoint item id or sharing URL is invalid.");

        var client = httpClientFactory.CreateClient("GccV2MicrosoftGraph");
        var metaUrl = isShareUrl
            ? $"https://graph.microsoft.com/v1.0/shares/{Uri.EscapeDataString(ToShareId(itemRef))}/driveItem"
            : $"https://graph.microsoft.com/v1.0/me/drive/items/{Uri.EscapeDataString(itemRef)}";

        using var metaRequest = new HttpRequestMessage(HttpMethod.Get, metaUrl);
        metaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var metaResponse = await client.SendAsync(metaRequest, ct);
        if (!metaResponse.IsSuccessStatusCode)
        {
            var body = await metaResponse.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"SharePoint metadata failed (HTTP {(int)metaResponse.StatusCode}): {Trim(body)}");
        }

        await using var metaStream = await metaResponse.Content.ReadAsStreamAsync(ct);
        using var metaDoc = await JsonDocument.ParseAsync(metaStream, cancellationToken: ct);
        var id = metaDoc.RootElement.TryGetProperty("id", out var idEl)
            ? idEl.GetString()?.Trim() ?? itemRef
            : itemRef;
        var name = metaDoc.RootElement.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()?.Trim() ?? id
            : id;
        var mimeType = "";
        if (metaDoc.RootElement.TryGetProperty("file", out var fileEl)
            && fileEl.TryGetProperty("mimeType", out var mimeEl))
        {
            mimeType = mimeEl.GetString()?.Trim() ?? "";
        }

        DateTimeOffset? modified = null;
        if (metaDoc.RootElement.TryGetProperty("lastModifiedDateTime", out var modifiedEl)
            && DateTimeOffset.TryParse(modifiedEl.GetString(), out var parsedModified))
        {
            modified = parsedModified;
        }

        var webUrl = metaDoc.RootElement.TryGetProperty("webUrl", out var webEl)
            ? webEl.GetString()?.Trim() ?? itemRef
            : itemRef;

        if (string.IsNullOrWhiteSpace(mimeType))
            throw new InvalidOperationException("SharePoint item is not a downloadable file.");

        if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Audio and video SharePoint files remain fail-closed until local transcription is configured.");
        }

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            if (!GccV2LocalOcrEnv.IsConfigured || !GccV2LocalOcrEnv.IsImageMediaType(mimeType))
            {
                throw new InvalidOperationException(
                    "Image SharePoint files remain fail-closed until local OCR is configured (GEEK_CC_OCR_COMMAND + GEEK_CC_OCR_DATA_PROCESSING_APPROVED=true).");
            }
        }
        else if (!DownloadableMime.Contains(mimeType)
            && !mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            && !mimeType.Contains("officedocument", StringComparison.OrdinalIgnoreCase)
            && !mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SharePoint mime type '{mimeType}' is not in the approved Knowledge parser set.");
        }

        var contentUrl = isShareUrl
            ? $"https://graph.microsoft.com/v1.0/shares/{Uri.EscapeDataString(ToShareId(itemRef))}/driveItem/content"
            : $"https://graph.microsoft.com/v1.0/me/drive/items/{Uri.EscapeDataString(id)}/content";
        var bytes = await DownloadAsync(client, accessToken, contentUrl, ct);
        if (bytes.Length == 0)
            throw new InvalidOperationException("SharePoint file download returned empty content.");

        var mediaType = NormalizeMediaType(mimeType);
        var fileName = EnsureExtension(SanitizeFileName(name), mediaType);
        return new GccV2SharePointFileContent(id, name, mimeType, mediaType, fileName, bytes, modified, webUrl);
    }

    public static string StubMarkdown(string itemIdOrUrl, string accountLabel)
    {
        TryNormalizeItemRef(itemIdOrUrl, out var itemRef, out var isShare);
        var label = isShare ? "share-url" : itemRef;
        if (string.IsNullOrWhiteSpace(label)) label = "stub-item";
        var sb = new StringBuilder();
        sb.AppendLine($"# SharePoint stub · {label}");
        sb.AppendLine();
        sb.AppendLine($"- Connection: {accountLabel}");
        sb.AppendLine($"- Item: `{label}`");
        sb.AppendLine($"- Fetched: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine();
        sb.AppendLine(
            "Stub SharePoint connections do not call Microsoft Graph. Connect with OAuth to import live file bytes.");
        return sb.ToString();
    }

    private async Task<GccV2SharePointTokenResponse> TokenRequestAsync(
        IReadOnlyDictionary<string, string> formFields, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("GccV2MicrosoftGraph");
        using var content = new FormUrlEncodedContent(formFields);
        using var response = await client.PostAsync(GccV2SharePointOAuthEnv.TokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Microsoft token exchange failed: {Trim(body)}");
        using var doc = JsonDocument.Parse(body);
        var access = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Microsoft token response missing access_token.");
        var refresh = doc.RootElement.TryGetProperty("refresh_token", out var refreshEl)
            ? refreshEl.GetString()
            : null;
        return new GccV2SharePointTokenResponse(access, refresh);
    }

    private static async Task<byte[]> DownloadAsync(
        HttpClient client, string accessToken, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"SharePoint download failed (HTTP {(int)response.StatusCode}): {Trim(body)}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static string NormalizeMediaType(string mimeType)
    {
        if (GccV2LocalOcrEnv.IsImageMediaType(mimeType))
            return GccV2LocalOcrEnv.NormalizeImageMediaType(mimeType);
        return mimeType.Equals("text/markdown", StringComparison.OrdinalIgnoreCase) ? "text/markdown"
            : mimeType.Equals("text/html", StringComparison.OrdinalIgnoreCase) ? "text/html"
            : mimeType.Equals("text/csv", StringComparison.OrdinalIgnoreCase) ? "text/csv"
            : mimeType.Equals("text/plain", StringComparison.OrdinalIgnoreCase) ? "text/plain"
            : mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf"
            : mimeType;
    }

    private static string EnsureExtension(string baseName, string mediaType)
    {
        var ext = mediaType switch
        {
            "text/markdown" => ".md",
            "text/html" => ".html",
            "text/csv" => ".csv",
            "text/plain" => ".txt",
            "application/pdf" => ".pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/tiff" => ".tiff",
            _ => Path.HasExtension(baseName) ? "" : ".bin",
        };
        if (string.IsNullOrEmpty(ext)) return baseName;
        return baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? baseName : baseName + ext;
    }

    private static string SanitizeFileName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '.' or '_' ? ch : '-').ToArray();
        var cleaned = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "sharepoint-file" : cleaned;
    }

    private static string Trim(string value) =>
        value.Length > 240 ? value[..240] : value;
}
