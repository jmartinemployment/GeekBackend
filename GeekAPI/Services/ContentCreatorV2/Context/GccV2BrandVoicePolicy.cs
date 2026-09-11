using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Typed Brand Voice policy overlay nested under Brand Kit <c>KitJson.voicePolicy</c>.
/// Gateable avoid/banned phrases only — identity samples stay soft in the kit body.
/// </summary>
public static class GccV2BrandVoicePolicy
{
    public const int CurrentSchemaVersion = 1;

    public static string? Validate(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null; // optional overlay
        if (payload.ValueKind != JsonValueKind.Object)
            return "Brand Voice policy must be a JSON object.";

        if (payload.TryGetProperty("schemaVersion", out var schemaVersion))
        {
            if (schemaVersion.ValueKind != JsonValueKind.Number
                || !schemaVersion.TryGetInt32(out var version)
                || version < 1
                || version > CurrentSchemaVersion)
            {
                return $"Brand Voice schemaVersion must be an integer from 1 to {CurrentSchemaVersion}.";
            }
        }

        if (payload.TryGetProperty("customInstructions", out var instructions)
            && instructions.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            return "Brand Voice customInstructions must be a string or null.";

        foreach (var listName in new[]
                 {
                     "toneAttributes", "preferredPhrases", "avoidPhrases", "bannedClaims",
                 })
        {
            var error = ValidateStringList(payload, listName);
            if (error is not null) return error;
        }

        foreach (var listName in new[] { "avoidPhrases", "bannedClaims", "preferredPhrases" })
        {
            if (!payload.TryGetProperty(listName, out var list) || list.ValueKind != JsonValueKind.Array)
                continue;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in list.EnumerateArray())
            {
                var value = entry.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!seen.Add(value))
                    return $"Brand Voice {listName} must be unique.";
            }
        }

        return null;
    }

    public static string? ValidateKitJson(string? kitJson)
    {
        if (string.IsNullOrWhiteSpace(kitJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(kitJson);
            if (!document.RootElement.TryGetProperty("voicePolicy", out var voicePolicy))
                return null;
            return Validate(voicePolicy);
        }
        catch (JsonException)
        {
            return "Brand Kit JSON is invalid.";
        }
    }

    private static string? ValidateStringList(JsonElement payload, string listName)
    {
        if (!payload.TryGetProperty(listName, out var list) || list.ValueKind == JsonValueKind.Null)
            return null;
        if (list.ValueKind != JsonValueKind.Array)
            return $"Brand Voice {listName} must be an array of strings.";
        if (list.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(x.GetString())))
        {
            return $"Brand Voice {listName} entries must be non-empty strings.";
        }
        return null;
    }
}
