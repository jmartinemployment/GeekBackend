using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.ContentCreatorV2.Drive;

public sealed record GccV2DriveFileContent(
    string FileId,
    string Name,
    string MimeType,
    string MediaType,
    string FileName,
    byte[] Bytes,
    DateTimeOffset? ModifiedAtUtc,
    string WebViewLink);

/// <summary>Google Drive v3 metadata + export/download for approved Knowledge media types.</summary>
public sealed class GccV2DriveFilesClient(IHttpClientFactory httpClientFactory)
{
    private static readonly Regex FileIdFromPath = new(
        @"/(?:file|document|spreadsheets|presentation)/d/([a-zA-Z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FileIdQuery = new(
        @"[?&]id=([a-zA-Z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> GoogleExportMime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/vnd.google-apps.document"] = "text/plain",
        ["application/vnd.google-apps.spreadsheet"] = "text/csv",
        ["application/vnd.google-apps.presentation"] = "text/plain",
    };

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
    };

    public static bool TryParseFileId(string? fileIdOrUrl, out string fileId)
    {
        fileId = "";
        var raw = (fileIdOrUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var pathMatch = FileIdFromPath.Match(raw);
            if (pathMatch.Success)
            {
                fileId = pathMatch.Groups[1].Value;
                return true;
            }

            var queryMatch = FileIdQuery.Match(raw);
            if (queryMatch.Success)
            {
                fileId = queryMatch.Groups[1].Value;
                return true;
            }

            return false;
        }

        if (raw.Length is < 10 or > 128) return false;
        if (!raw.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-')) return false;
        fileId = raw;
        return true;
    }

    public async Task<(string Email, string Name)> GetUserInfoAsync(string accessToken, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("GccV2GoogleApis");
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var email = doc.RootElement.TryGetProperty("email", out var emailEl)
            ? emailEl.GetString()?.Trim() ?? ""
            : "";
        var name = doc.RootElement.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()?.Trim() ?? ""
            : "";
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Google userinfo did not return an email.");
        return (email, name);
    }

    public async Task<GccV2DriveFileContent> FetchFileAsync(
        string accessToken, string fileId, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("GccV2GoogleApis");
        using var metaRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}"
            + "?fields=id,name,mimeType,modifiedTime,webViewLink&supportsAllDrives=true");
        metaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var metaResponse = await client.SendAsync(metaRequest, ct);
        if (!metaResponse.IsSuccessStatusCode)
        {
            var body = await metaResponse.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Drive metadata failed (HTTP {(int)metaResponse.StatusCode}): {Trim(body)}");
        }

        await using var metaStream = await metaResponse.Content.ReadAsStreamAsync(ct);
        using var metaDoc = await JsonDocument.ParseAsync(metaStream, cancellationToken: ct);
        var id = metaDoc.RootElement.GetProperty("id").GetString() ?? fileId;
        var name = metaDoc.RootElement.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()?.Trim() ?? id
            : id;
        var mimeType = metaDoc.RootElement.TryGetProperty("mimeType", out var mimeEl)
            ? mimeEl.GetString()?.Trim() ?? ""
            : "";
        DateTimeOffset? modified = null;
        if (metaDoc.RootElement.TryGetProperty("modifiedTime", out var modifiedEl)
            && DateTimeOffset.TryParse(modifiedEl.GetString(), out var parsedModified))
        {
            modified = parsedModified;
        }

        var webView = metaDoc.RootElement.TryGetProperty("webViewLink", out var linkEl)
            ? linkEl.GetString()?.Trim() ?? $"https://drive.google.com/file/d/{id}/view"
            : $"https://drive.google.com/file/d/{id}/view";

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Image, audio, and video Drive files remain fail-closed until local OCR/transcription is configured.");
        }

        byte[] bytes;
        string mediaType;
        string fileName;
        if (GoogleExportMime.TryGetValue(mimeType, out var exportMime))
        {
            bytes = await DownloadAsync(
                client,
                accessToken,
                $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(id)}/export"
                + $"?mimeType={Uri.EscapeDataString(exportMime)}",
                ct);
            mediaType = exportMime == "text/csv" ? "text/csv" : "text/plain";
            fileName = SanitizeFileName(name) + (exportMime == "text/csv" ? ".csv" : ".txt");
        }
        else if (DownloadableMime.Contains(mimeType))
        {
            bytes = await DownloadAsync(
                client,
                accessToken,
                $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(id)}"
                + "?alt=media&supportsAllDrives=true",
                ct);
            mediaType = NormalizeMediaType(mimeType);
            fileName = EnsureExtension(SanitizeFileName(name), mediaType);
        }
        else
        {
            throw new InvalidOperationException(
                $"Drive mime type '{mimeType}' is not in the approved Knowledge parser set.");
        }

        if (bytes.Length == 0)
            throw new InvalidOperationException("Drive file download returned empty content.");

        return new GccV2DriveFileContent(id, name, mimeType, mediaType, fileName, bytes, modified, webView);
    }

    public static string StubMarkdown(string fileIdOrUrl, string accountLabel)
    {
        var id = TryParseFileId(fileIdOrUrl, out var parsed) ? parsed : "stub-file";
        var sb = new StringBuilder();
        sb.AppendLine($"# Drive stub · {id}");
        sb.AppendLine();
        sb.AppendLine($"- Connection: {accountLabel}");
        sb.AppendLine($"- File id: `{id}`");
        sb.AppendLine($"- Fetched: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine();
        sb.AppendLine(
            "Stub Drive connections do not call Google. Connect with OAuth to import live file bytes.");
        return sb.ToString();
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
                $"Drive download failed (HTTP {(int)response.StatusCode}): {Trim(body)}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static string NormalizeMediaType(string mimeType) =>
        mimeType.Equals("text/markdown", StringComparison.OrdinalIgnoreCase) ? "text/markdown"
        : mimeType.Equals("text/html", StringComparison.OrdinalIgnoreCase) ? "text/html"
        : mimeType.Equals("text/csv", StringComparison.OrdinalIgnoreCase) ? "text/csv"
        : mimeType.Equals("text/plain", StringComparison.OrdinalIgnoreCase) ? "text/plain"
        : mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf"
        : mimeType;

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
            _ => ".bin",
        };
        return baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? baseName : baseName + ext;
    }

    private static string SanitizeFileName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '.' or '_' ? ch : '-').ToArray();
        var cleaned = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "drive-file" : cleaned;
    }

    private static string Trim(string value) =>
        value.Length > 240 ? value[..240] : value;
}
