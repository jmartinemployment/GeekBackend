using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Gsc;
using GeekAPI.Services.ContentCreatorV2.SharePoint;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Knowledge connector: owner-owned SharePoint connection → file revision via Microsoft Graph.
/// </summary>
public sealed class GccV2SharePointContextConnector(IServiceScopeFactory scopeFactory) : IGccV2ContextConnector
{
    public const string ConnectorId = "sharepoint";

    public string Id => ConnectorId;

    public bool CanRefresh(JsonElement sourceDescriptor)
    {
        if (sourceDescriptor.ValueKind != JsonValueKind.Object) return false;
        if (!TryReadString(sourceDescriptor, "connectorId", out var connectorId)
            && !TryReadString(sourceDescriptor, "type", out connectorId))
        {
            return false;
        }

        if (!string.Equals(connectorId, ConnectorId, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(connectorId, "sharepoint_connector", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(connectorId, "microsoft-sharepoint", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return (TryReadGuid(sourceDescriptor, "sharePointConnectionId", out _)
                || TryReadGuid(sourceDescriptor, "connectionId", out _))
            && (TryReadString(sourceDescriptor, "itemId", out _)
                || TryReadString(sourceDescriptor, "itemIdOrUrl", out _));
    }

    public async Task<GccV2ConnectorRevision> FetchAsync(
        JsonElement sourceDescriptor, string ownerUserId, CancellationToken ct)
    {
        if (!TryReadGuid(sourceDescriptor, "sharePointConnectionId", out var connectionId)
            && !TryReadGuid(sourceDescriptor, "connectionId", out connectionId))
        {
            throw new InvalidOperationException(
                "SharePoint connector sourceDescriptor requires sharePointConnectionId.");
        }

        if (!TryReadString(sourceDescriptor, "itemIdOrUrl", out var itemIdOrUrl)
            && !TryReadString(sourceDescriptor, "itemId", out itemIdOrUrl))
        {
            throw new InvalidOperationException("SharePoint connector sourceDescriptor requires itemId.");
        }

        if (!GccV2SharePointGraphClient.TryNormalizeItemRef(itemIdOrUrl, out var itemRef, out _))
            throw new InvalidOperationException("SharePoint connector itemId is invalid.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        var graph = scope.ServiceProvider.GetRequiredService<GccV2SharePointGraphClient>();

        var connection = await repository.GetSharePointConnectionAsync(connectionId, ownerUserId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("SharePoint connection was not found.");

        byte[] bytes;
        string mediaType;
        string title;
        string sourceUrl;
        DateTimeOffset sourceTimestamp;
        string sourceId;

        if (connection.Status == "stub" || connection.EncryptedRefreshToken.Length == 0)
        {
            var markdown = GccV2SharePointGraphClient.StubMarkdown(itemRef, connection.AccountLabel);
            bytes = Encoding.UTF8.GetBytes(markdown);
            mediaType = "text/markdown; charset=utf-8";
            title = $"SharePoint stub · {itemRef}";
            sourceUrl = itemRef;
            sourceTimestamp = DateTimeOffset.UtcNow;
            sourceId = $"sharepoint-stub:{connection.Id:D}";
        }
        else
        {
            if (!GccV2SharePointOAuthEnv.IsConfigured)
                throw new InvalidOperationException("SharePoint Microsoft OAuth is not configured.");

            var refresh = GccV2GscCredentialProtector.Decrypt(
                connection.EncryptedRefreshToken,
                connection.EncryptionIv,
                connection.EncryptionTag);
            var access = await graph.ExchangeRefreshTokenAsync(refresh, ct).ConfigureAwait(false);
            var file = await graph.FetchFileAsync(access, itemRef, ct).ConfigureAwait(false);
            bytes = file.Bytes;
            mediaType = file.MediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                ? $"{file.MediaType}; charset=utf-8"
                : file.MediaType;
            title = file.Name;
            sourceUrl = file.WebUrl;
            sourceTimestamp = file.ModifiedAtUtc ?? DateTimeOffset.UtcNow;
            sourceId = $"sharepoint:{connection.Id:D}:{file.ItemId}";
        }

        var stream = new MemoryStream(bytes, writable: false);
        var provenance = JsonSerializer.SerializeToElement(new
        {
            sourceLabel = title,
            sourceUrl,
            sourceTimestampUtc = sourceTimestamp,
            parser = nameof(GccV2SharePointContextConnector),
            parserVersion = "1",
            connectorId = ConnectorId,
            sharePointConnectionId = connection.Id.ToString("D"),
            itemIdOrUrl = itemRef,
            accountLabel = connection.AccountLabel,
            sourceId,
        });

        return new GccV2ConnectorRevision(
            stream,
            mediaType,
            title,
            sourceUrl,
            sourceTimestamp,
            sourceId,
            provenance);
    }

    private static bool TryReadString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
            return false;
        var raw = prop.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        value = raw;
        return true;
    }

    private static bool TryReadGuid(JsonElement element, string name, out Guid value)
    {
        value = Guid.Empty;
        if (!TryReadString(element, name, out var raw)) return false;
        return Guid.TryParse(raw, out value);
    }
}
