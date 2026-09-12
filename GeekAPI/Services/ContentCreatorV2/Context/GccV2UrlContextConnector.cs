using System.Text;
using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>First Knowledge connector: public http(s) URL → governed text revision.</summary>
public sealed class GccV2UrlContextConnector(IServiceScopeFactory scopeFactory) : IGccV2ContextConnector
{
    public const string ConnectorId = "url";

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
            && !string.Equals(connectorId, "url_connector", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryReadString(sourceDescriptor, "url", out _)
            || TryReadString(sourceDescriptor, "finalUrl", out _);
    }

    public async Task<GccV2ConnectorRevision> FetchAsync(
        JsonElement sourceDescriptor, string ownerUserId, CancellationToken ct)
    {
        _ = ownerUserId;
        if (!TryReadString(sourceDescriptor, "url", out var url)
            && !TryReadString(sourceDescriptor, "finalUrl", out url))
        {
            throw new InvalidOperationException("URL connector sourceDescriptor requires url.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var hydrator = scope.ServiceProvider.GetRequiredService<GccV2TaskAgentPageHydrator>();
        var outcome = await hydrator.HydrateAsync(url, ct).ConfigureAwait(false);
        if (!outcome.Ok || string.IsNullOrWhiteSpace(outcome.VisibleContent))
        {
            throw new InvalidOperationException(
                outcome.ErrorMessage ?? "URL connector could not fetch visible content.");
        }

        var bytes = Encoding.UTF8.GetBytes(outcome.VisibleContent);
        var stream = new MemoryStream(bytes, writable: false);
        var label = string.IsNullOrWhiteSpace(outcome.Title)
            ? outcome.FinalUrl ?? url
            : outcome.Title!;
        var provenance = JsonSerializer.SerializeToElement(new
        {
            sourceLabel = label,
            sourceUrl = outcome.FinalUrl ?? url,
            sourceTimestampUtc = DateTimeOffset.UtcNow,
            parser = nameof(GccV2TaskAgentPageHydrator),
            parserVersion = "1",
            contentCompleteness = outcome.ContentCompleteness,
            statusCode = outcome.StatusCode,
            hydrateEngine = outcome.Engine,
        });

        return new GccV2ConnectorRevision(
            stream,
            "text/markdown; charset=utf-8",
            label,
            outcome.FinalUrl ?? url,
            DateTimeOffset.UtcNow,
            outcome.FinalUrl ?? url,
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
}
