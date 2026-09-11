using System.Text;
using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>First Knowledge connector: public http(s) URL → governed text revision.</summary>
public sealed class GccV2UrlContextConnector : IGccV2ContextConnector
{
    public const string ConnectorId = "url";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GccV2SafeOutboundUrl.HostResolver? _resolve;

    public GccV2UrlContextConnector(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, resolve: null)
    {
    }

    public GccV2UrlContextConnector(
        IHttpClientFactory httpClientFactory,
        GccV2SafeOutboundUrl.HostResolver? resolve)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _resolve = resolve;
    }

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

        var http = _httpClientFactory.CreateClient(nameof(GccV2TaskAgentPageHydrator));
        var hydrator = new GccV2TaskAgentPageHydrator(http);
        var outcome = await hydrator.HydrateAsync(url, ct, _resolve).ConfigureAwait(false);
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
