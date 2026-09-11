using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2UrlKnowledgeResult(
    Guid AssetId,
    Guid VersionId,
    Guid ResourceId,
    string FinalUrl,
    string Title,
    string ContentCompleteness,
    int StatusCode,
    long ByteSize,
    string ContentSha256,
    string IngestionState,
    Guid IngestionJobId);

public sealed class GccV2UrlKnowledgeService(
    HttpGccV2Repository repository,
    IGccV2ContextObjectStore objectStore,
    GccV2ContextIngestionWake ingestionWake,
    GccV2TaskAgentPageHydrator pageHydrator,
    IConfiguration configuration)
{
    public async Task<(GccV2UrlKnowledgeResult? Result, HttpStatusCode Status, string? Error, string? ErrorCode)>
        IngestAsync(
            string ownerUserId,
            string? url,
            string? name,
            IReadOnlyList<string>? tags,
            Guid? assetId,
            CancellationToken ct)
    {
        var outcome = await pageHydrator.HydrateAsync(url, ct).ConfigureAwait(false);
        if (!outcome.Ok || string.IsNullOrWhiteSpace(outcome.VisibleContent))
        {
            return (null, outcome.HttpStatus, outcome.ErrorMessage ?? "Could not fetch page content.",
                outcome.ErrorCode ?? "extract");
        }

        var bytes = Encoding.UTF8.GetBytes(outcome.VisibleContent);
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
        var finalUrl = outcome.FinalUrl ?? url!.Trim();
        var host = Uri.TryCreate(finalUrl, UriKind.Absolute, out var finalUri)
            ? finalUri.Host
            : "url";
        var title = string.IsNullOrWhiteSpace(outcome.Title) ? host : outcome.Title!.Trim();
        var assetName = string.IsNullOrWhiteSpace(name) ? title : name.Trim();
        var tagList = new List<string> { "url", host };
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
                assetName,
                $"Fetched from {finalUrl}",
                JsonSerializer.Serialize(tagList),
                "url_document",
                ownerUserId), ct).ConfigureAwait(false);
        if (asset is null)
            return (null, HttpStatusCode.NotFound, "Knowledge asset was not found.", "not_found");

        var fetchedAt = DateTimeOffset.UtcNow;
        var sourceDescriptor = new
        {
            type = "url_connector",
            connectorId = GccV2UrlContextConnector.ConnectorId,
            url = url!.Trim(),
            finalUrl,
            fetchedAtUtc = fetchedAt,
            title,
            statusCode = outcome.StatusCode,
            contentCompleteness = outcome.ContentCompleteness,
            loadTimeMs = outcome.LoadTimeMs,
        };
        var provenance = new
        {
            sourceLabel = title,
            sourceUrl = finalUrl,
            sourceTimestampUtc = fetchedAt,
            parser = nameof(GccV2TaskAgentPageHydrator),
            parserVersion = "1",
            contentDigest = $"sha256:{sha256}",
            contentCompleteness = outcome.ContentCompleteness,
            statusCode = outcome.StatusCode,
        };
        var canonical = GccV2CanonicalJson.Serialize(new
        {
            assetId = asset.Id,
            schemaVersion = 1,
            contentSha256 = sha256,
            mediaType = "text/markdown",
            sourceDescriptor,
        });
        var version = await repository.CreateKnowledgeVersionAsync(asset.Id, new(
            ownerUserId,
            1,
            GccV2CanonicalJson.Sha256(canonical),
            sha256,
            "text/markdown",
            "en",
            JsonSerializer.Serialize(sourceDescriptor),
            JsonSerializer.Serialize(provenance),
            fetchedAt,
            ownerUserId), ct).ConfigureAwait(false);

        var objectKey =
            $"content-creator-v2/{ownerUserId}/knowledge/{asset.Id}/{version.Id:N}/original.md";
        await using (var stream = new MemoryStream(bytes, writable: false))
        {
            await objectStore.PutAsync(objectKey, stream, "text/markdown; charset=utf-8", ct)
                .ConfigureAwait(false);
        }

        var safeName = $"{SanitizeFileName(host)}.md";
        var resource = await repository.AddKnowledgeResourceAsync(version.Id, new(
            ownerUserId,
            "original",
            objectKey,
            bytes.LongLength,
            sha256,
            "text/markdown; charset=utf-8",
            safeName,
            "quarantined",
            CoordinatesJson: JsonSerializer.Serialize(new { finalUrl, title })), ct)
            .ConfigureAwait(false);

        var ingestion = await repository.QueueKnowledgeIngestionAsync(version.Id, new(
            ownerUserId, objectKey, bytes.LongLength, sha256), ct).ConfigureAwait(false);
        ingestionWake.Wake(ingestion.Id);

        return (new GccV2UrlKnowledgeResult(
            asset.Id,
            version.Id,
            resource.Id,
            finalUrl,
            title,
            outcome.ContentCompleteness ?? "partial",
            outcome.StatusCode ?? 0,
            bytes.LongLength,
            sha256,
            string.IsNullOrWhiteSpace(ingestion.Status) ? "queued" : ingestion.Status,
            ingestion.Id), HttpStatusCode.Accepted, null, null);
    }

    private static string SanitizeFileName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '.' ? ch : '-').ToArray();
        var cleaned = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "page" : cleaned;
    }
}
