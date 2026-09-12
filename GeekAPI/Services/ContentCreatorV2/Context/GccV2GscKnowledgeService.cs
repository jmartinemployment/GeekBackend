using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Gsc;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2GscKnowledgeResult(
    Guid AssetId,
    Guid VersionId,
    Guid ResourceId,
    Guid GscConnectionId,
    string SiteUrl,
    string Title,
    int QueryCount,
    long ByteSize,
    string ContentSha256,
    string IngestionState,
    Guid IngestionJobId);

public sealed class GccV2GscKnowledgeService(
    HttpGccV2Repository repository,
    IGccV2ContextObjectStore objectStore,
    GccV2ContextIngestionWake ingestionWake,
    GccV2GscSearchAnalyticsClient search,
    IConfiguration configuration)
{
    public async Task<(GccV2GscKnowledgeResult? Result, HttpStatusCode Status, string? Error, string? ErrorCode)>
        IngestAsync(
            string ownerUserId,
            Guid gscConnectionId,
            DateOnly? startDate,
            DateOnly? endDate,
            int? rowLimit,
            string? name,
            IReadOnlyList<string>? tags,
            Guid? assetId,
            CancellationToken ct)
    {
        if (gscConnectionId == Guid.Empty)
        {
            return (null, HttpStatusCode.BadRequest, "gscConnectionId is required.", "validation");
        }

        var end = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = startDate ?? end.AddDays(-90);
        if (start > end)
            return (null, HttpStatusCode.BadRequest, "startDate must be on or before endDate.", "validation");

        var limit = rowLimit is null or < 1 ? 200 : Math.Min(rowLimit.Value, 1000);
        var fetched = await GccV2GscKnowledgeFormatter.FetchRowsAsync(
            repository, search, ownerUserId, gscConnectionId, start, end, limit, ct)
            .ConfigureAwait(false);
        if (!fetched.Ok)
        {
            return (null, fetched.Status, fetched.Error ?? "GSC Knowledge ingest failed.",
                fetched.ErrorCode ?? "gsc_fetch");
        }

        var markdown = GccV2GscKnowledgeFormatter.ToMarkdown(fetched);
        var bytes = Encoding.UTF8.GetBytes(markdown);
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
        var title = string.IsNullOrWhiteSpace(name)
            ? $"GSC queries · {fetched.SiteUrl}"
            : name.Trim();
        var tagList = new List<string> { "gsc", "search-console" };
        if (!string.IsNullOrWhiteSpace(fetched.SiteUrl))
            tagList.Add(fetched.SiteUrl);
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
                $"Observed Search Console queries for {fetched.SiteUrl}",
                JsonSerializer.Serialize(tagList),
                "gsc_document",
                ownerUserId), ct).ConfigureAwait(false);
        if (asset is null)
            return (null, HttpStatusCode.NotFound, "Knowledge asset was not found.", "not_found");

        var sourceDescriptor = new
        {
            type = "gsc_connector",
            connectorId = GccV2GscContextConnector.ConnectorId,
            gscConnectionId = fetched.ConnectionId.ToString("D"),
            siteUrl = fetched.SiteUrl,
            startDate = start.ToString("yyyy-MM-dd"),
            endDate = end.ToString("yyyy-MM-dd"),
            rowLimit = limit,
            fetchedAtUtc = fetched.FetchedAtUtc,
            queryCount = fetched.Rows.Count,
            sourceId = fetched.SourceId,
        };
        var provenance = new
        {
            sourceLabel = fetched.SiteUrl,
            sourceUrl = fetched.SiteUrl,
            sourceTimestampUtc = fetched.FetchedAtUtc,
            parser = nameof(GccV2GscContextConnector),
            parserVersion = "1",
            contentDigest = $"sha256:{sha256}",
            connectorId = GccV2GscContextConnector.ConnectorId,
            gscConnectionId = fetched.ConnectionId.ToString("D"),
            queryCount = fetched.Rows.Count,
            demandDisclaimer =
                "Observed GSC queries are first-party search analytics, not traffic, volume, ranking, or demand scores.",
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
            fetched.FetchedAtUtc,
            ownerUserId), ct).ConfigureAwait(false);

        var objectKey =
            $"content-creator-v2/{ownerUserId}/knowledge/{asset.Id}/{version.Id:N}/original.md";
        await using (var stream = new MemoryStream(bytes, writable: false))
        {
            await objectStore.PutAsync(objectKey, stream, "text/markdown; charset=utf-8", ct)
                .ConfigureAwait(false);
        }

        var safeName = $"{SanitizeFileName(fetched.SiteUrl)}.md";
        var resource = await repository.AddKnowledgeResourceAsync(version.Id, new(
            ownerUserId,
            "original",
            objectKey,
            bytes.LongLength,
            sha256,
            "text/markdown; charset=utf-8",
            safeName,
            "quarantined",
            CoordinatesJson: JsonSerializer.Serialize(new
            {
                siteUrl = fetched.SiteUrl,
                gscConnectionId = fetched.ConnectionId,
                queryCount = fetched.Rows.Count,
            })), ct)
            .ConfigureAwait(false);

        var ingestion = await repository.QueueKnowledgeIngestionAsync(version.Id, new(
            ownerUserId, objectKey, bytes.LongLength, sha256), ct).ConfigureAwait(false);
        ingestionWake.Wake(ingestion.Id);

        return (new GccV2GscKnowledgeResult(
            asset.Id,
            version.Id,
            resource.Id,
            fetched.ConnectionId,
            fetched.SiteUrl,
            title,
            fetched.Rows.Count,
            bytes.LongLength,
            sha256,
            string.IsNullOrWhiteSpace(ingestion.Status) ? "queued" : ingestion.Status,
            ingestion.Id), HttpStatusCode.Accepted, null, null);
    }

    private static string SanitizeFileName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '.' ? ch : '-').ToArray();
        var cleaned = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(cleaned) ? "gsc-queries" : cleaned;
    }
}
