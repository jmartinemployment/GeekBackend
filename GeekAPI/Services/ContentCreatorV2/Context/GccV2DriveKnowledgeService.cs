using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Drive;
using GeekAPI.Services.ContentCreatorV2.Gsc;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2DriveKnowledgeResult(
    Guid AssetId,
    Guid VersionId,
    Guid ResourceId,
    Guid DriveConnectionId,
    string AccountLabel,
    string FileId,
    string Title,
    string MediaType,
    long ByteSize,
    string ContentSha256,
    string IngestionState,
    Guid IngestionJobId);

public sealed class GccV2DriveKnowledgeService(
    HttpGccV2Repository repository,
    IGccV2ContextObjectStore objectStore,
    GccV2ContextIngestionWake ingestionWake,
    GccV2GscSearchAnalyticsClient googleOAuth,
    GccV2DriveFilesClient driveFiles,
    IConfiguration configuration)
{
    public async Task<(GccV2DriveKnowledgeResult? Result, HttpStatusCode Status, string? Error, string? ErrorCode)>
        IngestAsync(
            string ownerUserId,
            Guid driveConnectionId,
            string? fileIdOrUrl,
            string? name,
            IReadOnlyList<string>? tags,
            Guid? assetId,
            CancellationToken ct)
    {
        if (driveConnectionId == Guid.Empty)
            return (null, HttpStatusCode.BadRequest, "driveConnectionId is required.", "validation");
        if (!GccV2DriveFilesClient.TryParseFileId(fileIdOrUrl, out var fileId))
            return (null, HttpStatusCode.BadRequest, "fileId or Drive/Docs URL is required.", "validation");

        var connection = await repository.GetDriveConnectionAsync(driveConnectionId, ownerUserId, ct)
            .ConfigureAwait(false);
        if (connection is null)
            return (null, HttpStatusCode.NotFound, "Drive connection was not found.", "not_found");

        byte[] bytes;
        string mediaType;
        string fileName;
        string title;
        string sourceUrl;
        string sourceMime;
        DateTimeOffset sourceTimestamp;
        string sourceId;

        if (connection.Status == "stub" || connection.EncryptedRefreshToken.Length == 0)
        {
            var markdown = GccV2DriveFilesClient.StubMarkdown(fileId, connection.AccountLabel);
            bytes = Encoding.UTF8.GetBytes(markdown);
            mediaType = "text/markdown";
            fileName = $"{Sanitize(fileId)}.md";
            title = string.IsNullOrWhiteSpace(name) ? $"Drive stub · {fileId}" : name.Trim();
            sourceUrl = $"https://drive.google.com/file/d/{fileId}/view";
            sourceMime = "text/markdown";
            sourceTimestamp = DateTimeOffset.UtcNow;
            sourceId = $"drive-stub:{connection.Id:D}:{fileId}";
        }
        else
        {
            if (!GccV2DriveOAuthEnv.IsConfigured)
            {
                return (null, HttpStatusCode.ServiceUnavailable,
                    "Drive Google OAuth is not configured (client id/secret, redirect URI, encryption key).",
                    "oauth");
            }

            try
            {
                var refresh = GccV2GscCredentialProtector.Decrypt(
                    connection.EncryptedRefreshToken,
                    connection.EncryptionIv,
                    connection.EncryptionTag);
                var access = await googleOAuth.ExchangeRefreshTokenAsync(
                    refresh, GccV2DriveOAuthEnv.ClientId, GccV2DriveOAuthEnv.ClientSecret, ct)
                    .ConfigureAwait(false);
                var file = await driveFiles.FetchFileAsync(access, fileId, ct).ConfigureAwait(false);
                bytes = file.Bytes;
                mediaType = file.MediaType;
                fileName = file.FileName;
                title = string.IsNullOrWhiteSpace(name) ? file.Name : name.Trim();
                sourceUrl = file.WebViewLink;
                sourceMime = file.MimeType;
                sourceTimestamp = file.ModifiedAtUtc ?? DateTimeOffset.UtcNow;
                sourceId = $"drive:{connection.Id:D}:{file.FileId}:{sourceTimestamp:yyyyMMddHHmmss}";
            }
            catch (Exception ex)
            {
                return (null, HttpStatusCode.BadGateway, ex.Message, "drive_fetch");
            }
        }

        var usage = await repository.GetContextQuotaUsageAsync(ownerUserId, ct: ct).ConfigureAwait(false);
        if (usage is null)
            return (null, HttpStatusCode.ServiceUnavailable, "Source-library quota is unavailable.", "quota");

        var maximumOwnerBytes = configuration.GetValue<long?>(
            "ContentCreatorV2:ContextObjectStore:MaxOwnerBytes") ?? 1024L * 1024 * 1024;
        if (usage.KnowledgeBytes + usage.OwnerAttachmentBytes + bytes.LongLength > maximumOwnerBytes)
        {
            return (null, HttpStatusCode.RequestEntityTooLarge,
                $"Owner context quota of {maximumOwnerBytes} bytes would be exceeded.", "quota");
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var tagList = new List<string> { "drive", "google-drive" };
        if (!string.IsNullOrWhiteSpace(connection.AccountLabel))
            tagList.Add(connection.AccountLabel);
        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                var cleaned = tag.Trim();
                if (!tagList.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                    tagList.Add(cleaned);
            }
        }

        var asset = assetId is { } existingId
            ? await repository.GetKnowledgeAsync(existingId, ownerUserId, ct).ConfigureAwait(false)
            : await repository.CreateKnowledgeAsync(new(
                ownerUserId,
                title,
                $"Google Drive file {fileId} via {connection.AccountLabel}",
                JsonSerializer.Serialize(tagList),
                "drive_document",
                ownerUserId), ct).ConfigureAwait(false);
        if (asset is null)
            return (null, HttpStatusCode.NotFound, "Knowledge asset was not found.", "not_found");

        var sourceDescriptor = new
        {
            type = "drive_connector",
            connectorId = GccV2DriveContextConnector.ConnectorId,
            driveConnectionId = connection.Id.ToString("D"),
            accountLabel = connection.AccountLabel,
            fileId,
            sourceUrl,
            sourceMime,
            mediaType,
            fetchedAtUtc = DateTimeOffset.UtcNow,
            sourceId,
        };
        var provenance = new
        {
            sourceLabel = title,
            sourceUrl,
            sourceTimestampUtc = sourceTimestamp,
            parser = nameof(GccV2DriveContextConnector),
            parserVersion = "1",
            contentDigest = $"sha256:{sha256}",
            connectorId = GccV2DriveContextConnector.ConnectorId,
            driveConnectionId = connection.Id.ToString("D"),
            fileId,
        };
        var canonical = GccV2CanonicalJson.Serialize(new
        {
            assetId = asset.Id,
            schemaVersion = 1,
            contentSha256 = sha256,
            mediaType,
            sourceDescriptor,
        });
        var version = await repository.CreateKnowledgeVersionAsync(asset.Id, new(
            ownerUserId,
            1,
            GccV2CanonicalJson.Sha256(canonical),
            sha256,
            mediaType,
            "en",
            JsonSerializer.Serialize(sourceDescriptor),
            JsonSerializer.Serialize(provenance),
            sourceTimestamp,
            ownerUserId), ct).ConfigureAwait(false);

        var objectKey =
            $"content-creator-v2/{ownerUserId}/knowledge/{asset.Id}/{version.Id:N}/{fileName}";
        var contentType = mediaType.Contains("charset", StringComparison.OrdinalIgnoreCase)
            ? mediaType
            : mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                ? $"{mediaType}; charset=utf-8"
                : mediaType;
        await using (var stream = new MemoryStream(bytes, writable: false))
        {
            await objectStore.PutAsync(objectKey, stream, contentType, ct).ConfigureAwait(false);
        }

        var resource = await repository.AddKnowledgeResourceAsync(version.Id, new(
            ownerUserId,
            "original",
            objectKey,
            bytes.LongLength,
            sha256,
            contentType,
            fileName,
            "quarantined",
            CoordinatesJson: JsonSerializer.Serialize(new
            {
                fileId,
                driveConnectionId = connection.Id,
                sourceUrl,
            })), ct).ConfigureAwait(false);

        var ingestion = await repository.QueueKnowledgeIngestionAsync(version.Id, new(
            ownerUserId, objectKey, bytes.LongLength, sha256), ct).ConfigureAwait(false);
        ingestionWake.Wake(ingestion.Id);

        return (new GccV2DriveKnowledgeResult(
            asset.Id,
            version.Id,
            resource.Id,
            connection.Id,
            connection.AccountLabel,
            fileId,
            title,
            mediaType,
            bytes.LongLength,
            sha256,
            string.IsNullOrWhiteSpace(ingestion.Status) ? "queued" : ingestion.Status,
            ingestion.Id), HttpStatusCode.Accepted, null, null);
    }

    private static string Sanitize(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray();
        var cleaned = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "drive-file" : cleaned;
    }
}
