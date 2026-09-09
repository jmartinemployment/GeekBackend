using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Typed Audience policy (who the content is for), separate from Style Guide and Brand Voice.
/// Schema version 1 covers persona identity, language preferences, and topical bans.
/// </summary>
public static class GccV2AudiencePolicy
{
    public const int CurrentSchemaVersion = 1;

    private static readonly HashSet<string> AllowedReadingLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "general",
        "professional",
        "expert",
        "executive",
    };

    public static string? Validate(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "Audience policy payload is required.";
        if (payload.ValueKind != JsonValueKind.Object)
            return "Audience policy must be a JSON object.";

        if (payload.TryGetProperty("schemaVersion", out var schemaVersion))
        {
            if (schemaVersion.ValueKind != JsonValueKind.Number
                || !schemaVersion.TryGetInt32(out var version)
                || version < 1
                || version > CurrentSchemaVersion)
            {
                return $"Audience schemaVersion must be an integer from 1 to {CurrentSchemaVersion}.";
            }
        }

        if (payload.TryGetProperty("locale", out var locale) && locale.ValueKind != JsonValueKind.Null)
        {
            if (locale.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(locale.GetString()))
            {
                return "Audience locale must be a non-empty string when provided.";
            }
        }

        if (payload.TryGetProperty("summary", out var summary)
            && summary.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            return "Audience summary must be a string or null.";

        if (payload.TryGetProperty("positioningStatement", out var positioning)
            && positioning.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            return "Audience positioningStatement must be a string or null.";

        if (payload.TryGetProperty("customInstructions", out var instructions)
            && instructions.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            return "Audience customInstructions must be a string or null.";

        if (payload.TryGetProperty("readingLevel", out var readingLevel)
            && readingLevel.ValueKind != JsonValueKind.Null)
        {
            if (readingLevel.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(readingLevel.GetString())
                || !AllowedReadingLevels.Contains(readingLevel.GetString()!))
            {
                return "Audience readingLevel must be general, professional, expert, or executive.";
            }
        }

        foreach (var listName in new[]
                 {
                     "industries", "roles", "pains", "goals", "buyingTriggers", "useCases",
                     "preferredLanguage", "bannedTopics", "avoidPhrases",
                 })
        {
            var error = ValidateStringList(payload, listName);
            if (error is not null) return error;
        }

        if (payload.TryGetProperty("bannedTopics", out var bannedTopics)
            && bannedTopics.ValueKind == JsonValueKind.Array)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in bannedTopics.EnumerateArray())
            {
                var value = entry.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!seen.Add(value))
                    return "Audience bannedTopics must be unique.";
            }
        }

        var valuePropError = ValidateObjectArray(
            payload, "valuePropositions", ["title", "description"], require: "title");
        if (valuePropError is not null) return valuePropError;

        var objectionError = ValidateObjectArray(
            payload, "objectionResponses", ["objection", "response"], require: "objection");
        if (objectionError is not null) return objectionError;

        var characteristicError = ValidateObjectArray(
            payload, "additionalCharacteristics", ["key", "value"], require: "key");
        if (characteristicError is not null) return characteristicError;

        return null;
    }

    private static string? ValidateStringList(JsonElement payload, string listName)
    {
        if (!payload.TryGetProperty(listName, out var list) || list.ValueKind == JsonValueKind.Null)
            return null;
        if (list.ValueKind != JsonValueKind.Array)
            return $"Audience {listName} must be an array of strings.";
        if (list.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(x.GetString())))
        {
            return $"Audience {listName} entries must be non-empty strings.";
        }
        return null;
    }

    private static string? ValidateObjectArray(
        JsonElement payload, string listName, IReadOnlyList<string> allowedFields, string require)
    {
        if (!payload.TryGetProperty(listName, out var list) || list.ValueKind == JsonValueKind.Null)
            return null;
        if (list.ValueKind != JsonValueKind.Array)
            return $"Audience {listName} must be an array of objects.";
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                return $"Each Audience {listName} entry must be an object.";
            foreach (var property in entry.EnumerateObject())
            {
                if (!allowedFields.Contains(property.Name, StringComparer.Ordinal))
                    return $"Unknown Audience {listName} field: {property.Name}.";
                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return $"Audience {listName}.{property.Name} must be a string or null.";
            }
            if (!entry.TryGetProperty(require, out var required)
                || required.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(required.GetString()))
            {
                return $"Each Audience {listName} entry needs a non-empty {require}.";
            }
        }
        return null;
    }
}
