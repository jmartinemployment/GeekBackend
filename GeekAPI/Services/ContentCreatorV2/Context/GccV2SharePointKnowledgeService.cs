using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Gsc;
using GeekAPI.Services.ContentCreatorV2.SharePoint;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2SharePointKnowledgeResult(
    Guid AssetId,
    Guid VersionId,
    Guid ResourceId,
    Guid SharePointConnectionId,
    string AccountLabel,
    string ItemId,
    string Title,
    string MediaType,
    long ByteSize,
    string ContentSha256,
    string IngestionState,
    Guid IngestionJobId);

public sealed class GccV2SharePointKnowledgeService(
    HttpGccV2Repository repository,
    IGccV2ContextObjectStore objectStore,
    GccV2ContextIngestionWake ingestionWake,
    GccV2SharePointGraphClient graph,
    IConfiguration configuration)
{
    public async Task<(GccV2SharePointKnowledgeResult? Result, HttpStatusCode Status, string? Error, string? ErrorCode)>
        IngestAsync(
            string ownerUserId,
            Guid sharePointConnectionId,
            string? itemIdOrUrl,
            string? name,
            IReadOnlyList<string>? tags,
            Guid? assetId,
            CancellationToken ct)
    {
        if (sharePointConnectionId == Guid.Empty)
            return (null, HttpStatusCode.BadRequest, "sharePointConnectionId is required.", "validation");
        if (!GccV2SharePointGraphClient.TryNormalizeItemRef(itemIdOrUrl, out var itemRef, out _))
            return (null, HttpStatusCode.BadRequest, "itemId or SharePoint/OneDrive sharing URL is required.", "validation");

        var connection = await repository.GetSharePointConnectionAsync(sharePointConnectionId, ownerUserId, ct)
            .ConfigureAwait(false);
        if (connection is null)
            return (null, HttpStatusCode.NotFound, "SharePoint connection was not found.", "not_found");

        byte[] bytes;
        string mediaType;
        string fileName;
        string title;
        string sourceUrl;
        string sourceMime;
        DateTimeOffset sourceTimestamp;
        string sourceId;
        string itemId;

        if (connection.Status == "stub" || connection.EncryptedRefreshToken.Length == 0)
        {
            var markdown = GccV2SharePointGraphClient.StubMarkdown(itemRef, connection.AccountLabel);
            bytes = Encoding.UTF8.GetBytes(markdown);
            mediaType = "text/markdown";
            fileName = $"{Sanitize(itemRef)}.md";
            title = string.IsNullOrWhiteSpace(name) ? $"SharePoint stub · {Sanitize(itemRef)}" : name.Trim();
            sourceUrl = itemRef.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? itemRef
                : $"sharepoint://item/{itemRef}";
            sourceMime = "text/markdown";
            sourceTimestamp = DateTimeOffset.UtcNow;
            sourceId = $"sharepoint-stub:{connection.Id:D}:{Sanitize(itemRef)}";
            itemId = Sanitize(itemRef);
        }
        else
        {
            if (!GccV2SharePointOAuthEnv.IsConfigured)
            {
                return (null, HttpStatusCode.ServiceUnavailable,
                    "SharePoint Microsoft OAuth is not configured (client id/secret, redirect URI, encryption key).",
                    "oauth");
            }

            try
            {
                var refresh = GccV2GscCredentialProtector.Decrypt(
                    connection.EncryptedRefreshToken,
                    connection.EncryptionIv,
                    connection.EncryptionTag);
                var access = await graph.ExchangeRefreshTokenAsync(refresh, ct).ConfigureAwait(false);
                var file = await graph.FetchFileAsync(access, itemRef, ct).ConfigureAwait(false);
                bytes = file.Bytes;
                mediaType = file.MediaType;
                fileName = file.FileName;
                title = string.IsNullOrWhiteSpace(name) ? file.Name : name.Trim();
                sourceUrl = file.WebUrl;
                sourceMime = file.MimeType;
                sourceTimestamp = file.ModifiedAtUtc ?? DateTimeOffset.UtcNow;
                sourceId = $"sharepoint:{connection.Id:D}:{file.ItemId}:{sourceTimestamp:yyyyMMddHHmmss}";
                itemId = file.ItemId;
            }
            catch (Exception ex)
            {
                return (null, HttpStatusCode.BadGateway, ex.Message, "sharepoint_fetch");
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
        var tagList = new List<string> { "sharepoint", "microsoft-graph" };
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
                $"SharePoint file {itemId} via {connection.AccountLabel}",
                JsonSerializer.Serialize(tagList),
                "sharepoint_document",
                ownerUserId), ct).ConfigureAwait(false);
        if (asset is null)
            return (null, HttpStatusCode.NotFound, "Knowledge asset was not found.", "not_found");

        var sourceDescriptor = new
        {
            type = "sharepoint_connector",
            connectorId = GccV2SharePointContextConnector.ConnectorId,
            sharePointConnectionId = connection.Id.ToString("D"),
            accountLabel = connection.AccountLabel,
            itemId,
            itemIdOrUrl = itemRef,
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
            parser = nameof(GccV2SharePointContextConnector),
            parserVersion = "1",
            contentDigest = $"sha256:{sha256}",
            connectorId = GccV2SharePointContextConnector.ConnectorId,
            sharePointConnectionId = connection.Id.ToString("D"),
            itemId,
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
                itemId,
                sharePointConnectionId = connection.Id,
                sourceUrl,
            })), ct).ConfigureAwait(false);

        var ingestion = await repository.QueueKnowledgeIngestionAsync(version.Id, new(
            ownerUserId, objectKey, bytes.LongLength, sha256), ct).ConfigureAwait(false);
        ingestionWake.Wake(ingestion.Id);

        return (new GccV2SharePointKnowledgeResult(
            asset.Id,
            version.Id,
            resource.Id,
            connection.Id,
            connection.AccountLabel,
            itemId,
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
        if (cleaned.Length > 80) cleaned = cleaned[..80];
        return string.IsNullOrWhiteSpace(cleaned) ? "sharepoint-file" : cleaned;
    }
}
