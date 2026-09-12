using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Drive;
using GeekAPI.Services.ContentCreatorV2.Gsc;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Knowledge connector: owner-owned Drive connection → file revision.
/// Reuses CC Google OAuth token exchange; does not introduce a second Google client stack.
/// </summary>
public sealed class GccV2DriveContextConnector(IServiceScopeFactory scopeFactory) : IGccV2ContextConnector
{
    public const string ConnectorId = "drive";

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
            && !string.Equals(connectorId, "drive_connector", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(connectorId, "google-drive", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return (TryReadGuid(sourceDescriptor, "driveConnectionId", out _)
                || TryReadGuid(sourceDescriptor, "connectionId", out _))
            && (TryReadString(sourceDescriptor, "fileId", out _)
                || TryReadString(sourceDescriptor, "fileIdOrUrl", out _));
    }

    public async Task<GccV2ConnectorRevision> FetchAsync(
        JsonElement sourceDescriptor, string ownerUserId, CancellationToken ct)
    {
        if (!TryReadGuid(sourceDescriptor, "driveConnectionId", out var connectionId)
            && !TryReadGuid(sourceDescriptor, "connectionId", out connectionId))
        {
            throw new InvalidOperationException("Drive connector sourceDescriptor requires driveConnectionId.");
        }

        if (!TryReadString(sourceDescriptor, "fileId", out var fileIdOrUrl)
            && !TryReadString(sourceDescriptor, "fileIdOrUrl", out fileIdOrUrl))
        {
            throw new InvalidOperationException("Drive connector sourceDescriptor requires fileId.");
        }

        if (!GccV2DriveFilesClient.TryParseFileId(fileIdOrUrl, out var fileId))
            throw new InvalidOperationException("Drive connector fileId is invalid.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        var googleOAuth = scope.ServiceProvider.GetRequiredService<GccV2GscSearchAnalyticsClient>();
        var driveFiles = scope.ServiceProvider.GetRequiredService<GccV2DriveFilesClient>();

        var connection = await repository.GetDriveConnectionAsync(connectionId, ownerUserId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Drive connection was not found.");

        byte[] bytes;
        string mediaType;
        string title;
        string sourceUrl;
        DateTimeOffset sourceTimestamp;
        string sourceId;

        if (connection.Status == "stub" || connection.EncryptedRefreshToken.Length == 0)
        {
            var markdown = GccV2DriveFilesClient.StubMarkdown(fileId, connection.AccountLabel);
            bytes = Encoding.UTF8.GetBytes(markdown);
            mediaType = "text/markdown; charset=utf-8";
            title = $"Drive stub · {fileId}";
            sourceUrl = $"https://drive.google.com/file/d/{fileId}/view";
            sourceTimestamp = DateTimeOffset.UtcNow;
            sourceId = $"drive-stub:{connection.Id:D}:{fileId}";
        }
        else
        {
            if (!GccV2DriveOAuthEnv.IsConfigured)
                throw new InvalidOperationException("Drive Google OAuth is not configured.");

            var refresh = GccV2GscCredentialProtector.Decrypt(
                connection.EncryptedRefreshToken,
                connection.EncryptionIv,
                connection.EncryptionTag);
            var access = await googleOAuth.ExchangeRefreshTokenAsync(
                refresh, GccV2DriveOAuthEnv.ClientId, GccV2DriveOAuthEnv.ClientSecret, ct)
                .ConfigureAwait(false);
            var file = await driveFiles.FetchFileAsync(access, fileId, ct).ConfigureAwait(false);
            bytes = file.Bytes;
            mediaType = file.MediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                ? $"{file.MediaType}; charset=utf-8"
                : file.MediaType;
            title = file.Name;
            sourceUrl = file.WebViewLink;
            sourceTimestamp = file.ModifiedAtUtc ?? DateTimeOffset.UtcNow;
            sourceId = $"drive:{connection.Id:D}:{file.FileId}";
        }

        var stream = new MemoryStream(bytes, writable: false);
        var provenance = JsonSerializer.SerializeToElement(new
        {
            sourceLabel = title,
            sourceUrl,
            sourceTimestampUtc = sourceTimestamp,
            parser = nameof(GccV2DriveContextConnector),
            parserVersion = "1",
            connectorId = ConnectorId,
            driveConnectionId = connection.Id.ToString("D"),
            fileId,
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
