using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Typed Product Schema policy: workspace field definitions for Product IQ records.
/// </summary>
public static class GccV2ProductSchemaPolicy
{
    public const int CurrentSchemaVersion = 1;

    private static readonly HashSet<string> AllowedValueTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "text", "number", "boolean",
    };

    public static string? Validate(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "Product Schema payload is required.";

        JsonElement fields = default;
        if (payload.ValueKind == JsonValueKind.Array)
            fields = payload;
        else if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("schemaVersion", out var schemaVersion))
            {
                if (schemaVersion.ValueKind != JsonValueKind.Number
                    || !schemaVersion.TryGetInt32(out var version)
                    || version < 1
                    || version > CurrentSchemaVersion)
                {
                    return $"Product Schema schemaVersion must be an integer from 1 to {CurrentSchemaVersion}.";
                }
            }
            if (!payload.TryGetProperty("fields", out fields))
                return "Product Schema must contain a fields array.";
        }
        else
            return "Product Schema must be a JSON object or fields array.";

        if (fields.ValueKind != JsonValueKind.Array)
            return "Product Schema fields must be an array.";
        if (fields.GetArrayLength() == 0)
            return "Product Schema must define at least one field.";

        var ids = new HashSet<Guid>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object)
                return "Each Product Schema field must be an object.";
            if (!field.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String
                || !Guid.TryParse(idElement.GetString(), out var id)
                || id == Guid.Empty)
            {
                return "Product Schema field IDs must be unique UUIDs.";
            }
            if (!ids.Add(id))
                return "Product Schema field IDs must be unique UUIDs.";
            if (!field.TryGetProperty("key", out var keyElement)
                || keyElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(keyElement.GetString()))
            {
                return "Each Product Schema field needs a non-empty key.";
            }
            if (!keys.Add(keyElement.GetString()!.Trim()))
                return "Product Schema field keys must be unique.";
            if (!field.TryGetProperty("label", out var label)
                || label.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(label.GetString()))
            {
                return "Each Product Schema field needs a non-empty label.";
            }
            if (field.TryGetProperty("required", out var required)
                && required.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return "Product Schema field.required must be a boolean.";
            }
            if (field.TryGetProperty("valueType", out var valueType)
                && valueType.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(valueType.GetString())
                && !AllowedValueTypes.Contains(valueType.GetString()!))
            {
                return "Product Schema field.valueType must be string, text, number, or boolean.";
            }
        }
        return null;
    }
}
