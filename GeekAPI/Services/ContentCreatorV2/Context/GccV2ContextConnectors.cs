using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Context;

public sealed record GccV2ConnectorRevision(
    Stream Content, string MediaType, string SourceLabel, string? SourceUrl,
    DateTimeOffset SourceModifiedAtUtc, string SourceVersion, JsonElement Provenance);

public interface IGccV2ContextConnector
{
    string Id { get; }
    bool CanRefresh(JsonElement sourceDescriptor);
    Task<GccV2ConnectorRevision> FetchAsync(
        JsonElement sourceDescriptor, string ownerUserId, CancellationToken ct);
}

public sealed class GccV2ContextConnectorRegistry(
    IEnumerable<IGccV2ContextConnector> connectors)
{
    private readonly IReadOnlyDictionary<string, IGccV2ContextConnector> _connectors =
        connectors.ToDictionary(x => x.Id, StringComparer.Ordinal);

    public IReadOnlyList<string> ConnectorIds => _connectors.Keys.Order(StringComparer.Ordinal).ToList();

    public IGccV2ContextConnector Resolve(string id) =>
        _connectors.TryGetValue(id, out var connector)
            ? connector
            : throw new InvalidOperationException($"Context connector '{id}' is not approved.");
}
