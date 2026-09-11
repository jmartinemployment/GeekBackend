using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

internal static class GccV2TaskInputSchemaValidator
{
    public static IReadOnlyList<string> Validate(JsonElement input, string schemaJson)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        var errors = new List<string>();
        ValidateValue(input, schema.RootElement, "$", errors);
        return errors;
    }

    private static void ValidateValue(
        JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (schema.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && !Matches(value, type.GetString()!))
            errors.Add($"{path} must be {type.GetString()}.");
        if (value.ValueKind != JsonValueKind.Object) return;
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var item in required.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && !value.TryGetProperty(item.GetString()!, out _))
                    errors.Add($"{path}.{item.GetString()} is required.");
        var hasProperties = schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object;
        if (hasProperties)
            foreach (var property in value.EnumerateObject())
                if (properties.TryGetProperty(property.Name, out var propertySchema))
                    ValidateValue(property.Value, propertySchema, $"{path}.{property.Name}", errors);
                else if (schema.TryGetProperty("additionalProperties", out var additional)
                    && additional.ValueKind == JsonValueKind.False)
                    errors.Add($"{path}.{property.Name} is not allowed.");
    }

    private static bool Matches(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true,
    };
}
