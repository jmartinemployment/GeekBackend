using System.Net;
using System.Text;
using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Gsc;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Knowledge connector: owner-owned GSC connection → observed query markdown revision.
/// Reuses Content Creator GSC OAuth; does not introduce a second Google client.
/// </summary>
public sealed class GccV2GscContextConnector(IServiceScopeFactory scopeFactory) : IGccV2ContextConnector
{
    public const string ConnectorId = "gsc";

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
            && !string.Equals(connectorId, "gsc_connector", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(connectorId, "google-search-console", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryReadGuid(sourceDescriptor, "gscConnectionId", out _)
            || TryReadGuid(sourceDescriptor, "connectionId", out _);
    }

    public async Task<GccV2ConnectorRevision> FetchAsync(
        JsonElement sourceDescriptor, string ownerUserId, CancellationToken ct)
    {
        if (!TryReadGuid(sourceDescriptor, "gscConnectionId", out var connectionId)
            && !TryReadGuid(sourceDescriptor, "connectionId", out connectionId))
        {
            throw new InvalidOperationException("GSC connector sourceDescriptor requires gscConnectionId.");
        }

        var end = TryReadDate(sourceDescriptor, "endDate")
            ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = TryReadDate(sourceDescriptor, "startDate") ?? end.AddDays(-90);
        var rowLimit = 200;
        if (sourceDescriptor.TryGetProperty("rowLimit", out var limitEl)
            && limitEl.TryGetInt32(out var parsedLimit)
            && parsedLimit > 0)
        {
            rowLimit = Math.Min(parsedLimit, 1000);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<HttpGccV2Repository>();
        var search = scope.ServiceProvider.GetRequiredService<GccV2GscSearchAnalyticsClient>();
        var fetched = await GccV2GscKnowledgeFormatter.FetchRowsAsync(
            repository, search, ownerUserId, connectionId, start, end, rowLimit, ct)
            .ConfigureAwait(false);
        if (!fetched.Ok)
            throw new InvalidOperationException(fetched.Error ?? "GSC connector fetch failed.");

        var markdown = GccV2GscKnowledgeFormatter.ToMarkdown(fetched);
        var bytes = Encoding.UTF8.GetBytes(markdown);
        var stream = new MemoryStream(bytes, writable: false);
        var provenance = JsonSerializer.SerializeToElement(new
        {
            sourceLabel = fetched.SiteUrl,
            sourceUrl = fetched.SiteUrl,
            sourceTimestampUtc = fetched.FetchedAtUtc,
            parser = nameof(GccV2GscContextConnector),
            parserVersion = "1",
            connectorId = ConnectorId,
            gscConnectionId = connectionId.ToString("D"),
            startDate = start.ToString("yyyy-MM-dd"),
            endDate = end.ToString("yyyy-MM-dd"),
            queryCount = fetched.Rows.Count,
            demandDisclaimer =
                "Observed GSC queries are first-party search analytics, not traffic, volume, ranking, or demand scores.",
        });

        return new GccV2ConnectorRevision(
            stream,
            "text/markdown; charset=utf-8",
            $"GSC queries · {fetched.SiteUrl}",
            fetched.SiteUrl,
            fetched.FetchedAtUtc,
            fetched.SourceId,
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

    private static DateOnly? TryReadDate(JsonElement element, string name)
    {
        if (!TryReadString(element, name, out var raw)) return null;
        return DateOnly.TryParse(raw, out var date) ? date : null;
    }
}

internal sealed record GccV2GscFetchOutcome(
    bool Ok,
    string? Error,
    string? ErrorCode,
    HttpStatusCode Status,
    Guid ConnectionId,
    string SiteUrl,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTimeOffset FetchedAtUtc,
    string SourceId,
    IReadOnlyList<GccV2GscQueryRow> Rows);

internal static class GccV2GscKnowledgeFormatter
{
    public static async Task<GccV2GscFetchOutcome> FetchRowsAsync(
        HttpGccV2Repository repository,
        GccV2GscSearchAnalyticsClient search,
        string ownerUserId,
        Guid connectionId,
        DateOnly start,
        DateOnly end,
        int rowLimit,
        CancellationToken ct)
    {
        var connection = await repository.GetGscConnectionAsync(connectionId, ownerUserId, ct)
            .ConfigureAwait(false);
        if (connection is null)
        {
            return new GccV2GscFetchOutcome(
                false, "GSC connection was not found.", "not_found", HttpStatusCode.NotFound,
                connectionId, "", start, end, DateTimeOffset.UtcNow, "", []);
        }

        var fetchedAt = DateTimeOffset.UtcNow;
        var sourceId = $"gsc:{connection.Id:D}:{start:yyyy-MM-dd}:{end:yyyy-MM-dd}";
        IReadOnlyList<GccV2GscQueryRow> rows;
        if (connection.Status == "stub" || connection.EncryptedRefreshToken.Length == 0)
        {
            rows = [];
        }
        else
        {
            if (!GccV2GscOAuthEnv.IsConfigured
                || string.IsNullOrWhiteSpace(GccV2GscOAuthEnv.ClientId)
                || string.IsNullOrWhiteSpace(GccV2GscOAuthEnv.ClientSecret))
            {
                return new GccV2GscFetchOutcome(
                    false,
                    "GEEK_CC_GSC_GOOGLE_CLIENT_ID/SECRET are required for live Search Console fetch.",
                    "oauth",
                    HttpStatusCode.ServiceUnavailable,
                    connection.Id,
                    connection.SiteUrl,
                    start,
                    end,
                    fetchedAt,
                    sourceId,
                    []);
            }

            try
            {
                var refresh = GccV2GscCredentialProtector.Decrypt(
                    connection.EncryptedRefreshToken,
                    connection.EncryptionIv,
                    connection.EncryptionTag);
                var access = await search.ExchangeRefreshTokenAsync(
                    refresh, GccV2GscOAuthEnv.ClientId, GccV2GscOAuthEnv.ClientSecret, ct)
                    .ConfigureAwait(false);
                rows = await search.QueryAsync(access, connection.SiteUrl, start, end, rowLimit, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return new GccV2GscFetchOutcome(
                    false, ex.Message, "gsc_fetch", HttpStatusCode.BadGateway,
                    connection.Id, connection.SiteUrl, start, end, fetchedAt, sourceId, []);
            }
        }

        return new GccV2GscFetchOutcome(
            true, null, null, HttpStatusCode.OK,
            connection.Id, connection.SiteUrl, start, end, fetchedAt, sourceId, rows);
    }

    public static string ToMarkdown(GccV2GscFetchOutcome fetched)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Observed Search Console queries · {fetched.SiteUrl}");
        sb.AppendLine();
        sb.AppendLine($"- Site: {fetched.SiteUrl}");
        sb.AppendLine($"- Window: {fetched.StartDate:yyyy-MM-dd} → {fetched.EndDate:yyyy-MM-dd}");
        sb.AppendLine($"- Fetched: {fetched.FetchedAtUtc:O}");
        sb.AppendLine($"- Source id: `{fetched.SourceId}`");
        sb.AppendLine();
        sb.AppendLine(
            "> Observed GSC queries are first-party search analytics, not traffic, volume, ranking, or demand scores.");
        sb.AppendLine();
        if (fetched.Rows.Count == 0)
        {
            sb.AppendLine("No observed queries were returned for this window.");
            return sb.ToString();
        }

        sb.AppendLine("| Query | Impressions | Clicks |");
        sb.AppendLine("| --- | ---: | ---: |");
        foreach (var row in fetched.Rows
            .OrderByDescending(x => x.Impressions)
            .ThenBy(x => x.Query, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("| ")
                .Append(EscapeCell(row.Query))
                .Append(" | ")
                .Append(row.Impressions)
                .Append(" | ")
                .Append(row.Clicks)
                .AppendLine(" |");
        }

        return sb.ToString();
    }

    private static string EscapeCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
