using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Results;

namespace GeekAPI.Services.Voice.Storage;

/// <summary>
/// Writes a finished call's record into the business's own Google Drive, under the single <see cref="GoogleDriveOptions.FolderName"/>
/// folder this application creates (plans/client-and-app-plan.md, "Google Drive (All Tiers)").
///
/// The connection holds only the <c>drive.file</c> scope, so every call here — finding the folder, making it, uploading — can reach
/// nothing in the owner's Drive but what this application itself created. Listing is how the folder is found again between calls and
/// is limited by that scope to this application's own files; there is no other read, and nothing is ever fetched back.
///
/// Drive has no paths, only folders, so the plan's <c>YYYY/MM/DD/call-...</c> convention is the file's name inside that one folder.
/// That keeps both halves of what the plan asks for: the naming it sets out, and strictly one folder. Dated subfolders would be a
/// second thing to create, and to get wrong, on every call.
///
/// Nothing of a call is logged: a failure is its HTTP status and nothing else.
/// </summary>
public sealed class GoogleDriveCallRecordStorage(
    GoogleAccessTokens tokens,
    IHttpClientFactory clients,
    ILogger<GoogleDriveCallRecordStorage> logger) : ICallRecordStorage
{
    public const string HttpClientName = "google-drive";

    /// <summary>The boundary of the multipart upload: Drive wants the metadata and the body in one request.</summary>
    private const string Boundary = "geek-receptionist-record";

    public string Provider => GoogleDriveOptions.Provider;

    public async Task<Result<string>> WriteAsync(Guid tenantId, CallRecordObject record, CancellationToken ct = default)
    {
        var token = await tokens.ForAsync(tenantId, GoogleDriveOptions.Provider, McpFailureCodes.StorageNotConnected, ct).ConfigureAwait(false);
        if (!token.IsSuccess) return Result<string>.Failure(token.Error!, token.ErrorCode!);

        var folder = await FolderIdAsync(token.Value!, ct).ConfigureAwait(false);
        if (!folder.IsSuccess) return Result<string>.Failure(folder.Error!, folder.ErrorCode!);

        return await UploadAsync(token.Value!, folder.Value!, record, ct).ConfigureAwait(false);
    }

    /// <summary>The id of this application's own logs folder, made the first time a record is written.</summary>
    private async Task<Result<string>> FolderIdAsync(string accessToken, CancellationToken ct)
    {
        var query = Uri.EscapeDataString(
            $"name = '{GoogleDriveOptions.FolderName}' and mimeType = '{GoogleDriveOptions.FolderMimeType}' and trashed = false");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{GoogleDriveOptions.FilesEndpoint}?q={query}&fields=files(id)&pageSize=1");
        var found = await SendAsync(request, accessToken, "finding the logs folder", ct).ConfigureAwait(false);
        if (!found.IsSuccess) return found;

        var files = JsonNode.Parse(found.Value!)?["files"]?.AsArray();
        var existing = files is { Count: > 0 } ? files[0]?["id"]?.GetValue<string>() : null;
        if (!string.IsNullOrWhiteSpace(existing)) return Result<string>.Success(existing);

        var metadata = new JsonObject
        {
            ["name"] = GoogleDriveOptions.FolderName,
            ["mimeType"] = GoogleDriveOptions.FolderMimeType,
        };
        using var create = new HttpRequestMessage(HttpMethod.Post, $"{GoogleDriveOptions.FilesEndpoint}?fields=id")
        {
            Content = new StringContent(metadata.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        var made = await SendAsync(create, accessToken, "creating the logs folder", ct).ConfigureAwait(false);
        if (!made.IsSuccess) return made;

        var id = JsonNode.Parse(made.Value!)?["id"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(id)
            ? Result<string>.Failure("Google Drive made the logs folder and gave no id for it.", McpFailureCodes.StorageWriteFailed)
            : Result<string>.Success(id);
    }

    private async Task<Result<string>> UploadAsync(string accessToken, string folderId, CallRecordObject record, CancellationToken ct)
    {
        var metadata = new JsonObject
        {
            ["name"] = record.Path,
            ["parents"] = new JsonArray(folderId),
        };

        var body = new MultipartContent("related", Boundary);
        var descriptor = new StringContent(metadata.ToJsonString(), Encoding.UTF8);
        descriptor.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "UTF-8" };
        body.Add(descriptor);

        var content = new StringContent(record.Body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(record.ContentType) { CharSet = "UTF-8" };
        body.Add(content);

        using var request = new HttpRequestMessage(HttpMethod.Post, GoogleDriveOptions.UploadEndpoint) { Content = body };
        var uploaded = await SendAsync(request, accessToken, "writing a call record", ct).ConfigureAwait(false);
        if (!uploaded.IsSuccess) return uploaded;

        var id = JsonNode.Parse(uploaded.Value!)?["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id))
            return Result<string>.Failure("Google Drive took the call record and gave no id for it.", McpFailureCodes.StorageWriteFailed);

        logger.LogInformation("Wrote a call record to a tenant's Google Drive folder.");
        return Result<string>.Success(id);
    }

    private async Task<Result<string>> SendAsync(HttpRequestMessage request, string accessToken, string what, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await clients.CreateClient(HttpClientName).SendAsync(request, ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return Result<string>.Success(text);

            // A token Google refuses is the owner's to grant again; nothing here renews it a second time or retries.
            var code = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? McpFailureCodes.ReauthorizationRequired
                : McpFailureCodes.StorageWriteFailed;
            logger.LogError("Google Drive refused {What} with {Status}.", what, (int)response.StatusCode);
            return Result<string>.Failure($"Google Drive refused {what}.", code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError("Google Drive could not be reached for {What} ({Failure}).", what, ex.GetType().Name);
            return Result<string>.Failure($"Google Drive could not be reached for {what}.", McpFailureCodes.StorageWriteFailed);
        }
    }
}
