using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeekAPI.Services.Workflow.Domain.Entities;

/// <summary>
/// Tolerant converter for persisted <see cref="LedeType"/> values.
/// The 12-type taxonomy is strict for new LLM output (see <see cref="Services.LlmResponseJsonParser"/>),
/// but a small number of legacy documents still carry "creative" which no longer exists.
/// This converter allows those documents to load with <c>null</c> (for <c>LedeType?</c>)
/// instead of throwing — the only acceptable fallback, requiring an explicit edit to set a valid 12 value.
/// Unknown strings map to null; known 12 values parse case-insensitively.
/// </summary>
public sealed class TolerantNullableLedeTypeConverter : JsonConverter<LedeType?>
{
    public override LedeType? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.String) throw new JsonException($"Expected string or null for LedeType, got {reader.TokenType}.");
        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (Enum.TryParse<LedeType>(raw.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(typeof(LedeType), parsed))
            return parsed;
        // Legacy "creative" and any other unknown → null (tolerant load, must be edited to a valid value)
        return null;
    }

    public override void Write(Utf8JsonWriter writer, LedeType? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.Value.ToString()));
    }
}

public sealed class StrictLedeTypeConverter : JsonConverter<LedeType>
{
    public override LedeType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException($"Expected string for LedeType, got {reader.TokenType}.");
        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw)) throw new JsonException("LedeType string was null/empty.");
        if (Enum.TryParse<LedeType>(raw.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(typeof(LedeType), parsed))
            return parsed;
        throw new JsonException($"Unknown LedeType '{raw}' — expected one of: {string.Join(", ", Enum.GetNames<LedeType>())}.");
    }

    public override void Write(Utf8JsonWriter writer, LedeType value, JsonSerializerOptions options)
        => writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
