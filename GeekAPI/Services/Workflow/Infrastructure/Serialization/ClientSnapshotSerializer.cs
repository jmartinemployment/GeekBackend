using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;

namespace GeekAPI.Services.Workflow.Infrastructure.Serialization;

/// <summary>
/// Serializes Client to/from JSON for durable persistence.
/// </summary>
public static class ClientSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TolerantNullableLedeTypeConverter(), new StrictLedeTypeConverter(), new JsonStringEnumConverter() }
    };

    public static string Serialize(Client client)
    {
        var snapshot = new ClientSnapshot(
            SchemaVersion: 1,
            Id: client.Id,
            Name: client.Name,
            Notes: client.Notes,
            CreatedAtUtc: client.CreatedAtUtc,
            PublishTarget: client.PublishTarget);

        return JsonSerializer.Serialize(snapshot, Options);
    }

    public static Client Deserialize(string json)
    {
        var snapshot = JsonSerializer.Deserialize<ClientSnapshot>(json, Options)
            ?? throw new InvalidOperationException("Failed to deserialize client snapshot.");

        return new Client
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            Notes = snapshot.Notes,
            CreatedAtUtc = snapshot.CreatedAtUtc,
            PublishTarget = snapshot.PublishTarget
        };
    }

    private sealed record ClientSnapshot(
        int SchemaVersion,
        Guid Id,
        string Name,
        string? Notes,
        DateTime CreatedAtUtc,
        PublishTarget? PublishTarget);
}
