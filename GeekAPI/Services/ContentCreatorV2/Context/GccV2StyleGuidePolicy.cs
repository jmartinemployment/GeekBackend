using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Typed Style Guide policy (generation-time constraints), separate from Brand Voice.
/// Schema version 1 covers grammar preferences, deterministic term rules, and custom instructions.
/// </summary>
public static class GccV2StyleGuidePolicy
{
    public const int CurrentSchemaVersion = 1;

    private static readonly HashSet<string> AllowedTermKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "prohibit",
        "replace",
        "capitalize",
        "abbreviation",
        "firstMention",
    };

    public static string? Validate(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "Style Guide policy payload is required.";
        if (payload.ValueKind != JsonValueKind.Object)
            return "Style Guide policy must be a JSON object.";

        if (payload.TryGetProperty("schemaVersion", out var schemaVersion))
        {
            if (schemaVersion.ValueKind != JsonValueKind.Number
                || !schemaVersion.TryGetInt32(out var version)
                || version < 1
                || version > CurrentSchemaVersion)
            {
                return $"Style Guide schemaVersion must be an integer from 1 to {CurrentSchemaVersion}.";
            }
        }

        if (payload.TryGetProperty("grammar", out var grammar) && grammar.ValueKind != JsonValueKind.Null)
        {
            if (grammar.ValueKind != JsonValueKind.Object)
                return "Style Guide grammar must be an object.";
            foreach (var property in grammar.EnumerateObject())
            {
                if (property.Name is not (
                    "oxfordComma" or "preferActiveVoice" or "allowEmDash" or "sentenceCaseHeadings"))
                {
                    return $"Unknown Style Guide grammar setting: {property.Name}.";
                }
                if (property.Value.ValueKind is not (
                    JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
                {
                    return $"Style Guide grammar.{property.Name} must be a boolean or null.";
                }
            }
        }

        if (payload.TryGetProperty("termRules", out var termRules) && termRules.ValueKind != JsonValueKind.Null)
        {
            if (termRules.ValueKind != JsonValueKind.Array)
                return "Style Guide termRules must be an array.";
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in termRules.EnumerateArray())
            {
                var error = ValidateTermRule(rule, seenIds);
                if (error is not null) return error;
            }
        }

        foreach (var listName in new[] { "prohibitedPhrases", "requiredPhrases" })
        {
            if (!payload.TryGetProperty(listName, out var list) || list.ValueKind == JsonValueKind.Null)
                continue;
            if (list.ValueKind != JsonValueKind.Array)
                return $"Style Guide {listName} must be an array of strings.";
            if (list.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(x.GetString())))
            {
                return $"Style Guide {listName} entries must be non-empty strings.";
            }
        }

        if (payload.TryGetProperty("customInstructions", out var instructions)
            && instructions.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return "Style Guide customInstructions must be a string or null.";
        }

        var prohibited = CollectPhrases(payload, "prohibit");
        var required = CollectPhrases(payload, "require");
        if (prohibited.Overlaps(required))
            return "A Style Guide phrase cannot be both prohibited and required.";

        return null;
    }

    private static string? ValidateTermRule(JsonElement rule, HashSet<string> seenIds)
    {
        if (rule.ValueKind != JsonValueKind.Object)
            return "Each Style Guide termRules entry must be an object.";
        if (!rule.TryGetProperty("kind", out var kindElement)
            || kindElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(kindElement.GetString())
            || !AllowedTermKinds.Contains(kindElement.GetString()!))
        {
            return "Style Guide termRules.kind must be prohibit, replace, capitalize, abbreviation, or firstMention.";
        }
        var kind = kindElement.GetString()!;
        if (!rule.TryGetProperty("match", out var match)
            || match.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(match.GetString()))
        {
            return "Style Guide termRules.match must be a non-empty string.";
        }
        if (rule.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(id.GetString()))
        {
            if (!seenIds.Add(id.GetString()!.Trim()))
                return "Style Guide termRules.id values must be unique.";
        }
        if (rule.TryGetProperty("caseSensitive", out var caseSensitive)
            && caseSensitive.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
        {
            return "Style Guide termRules.caseSensitive must be a boolean or null.";
        }
        if (rule.TryGetProperty("note", out var note)
            && note.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return "Style Guide termRules.note must be a string or null.";
        }

        var needsReplacement = kind is "replace" or "abbreviation" or "firstMention";
        var hasReplacement = rule.TryGetProperty("replacement", out var replacement)
            && replacement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(replacement.GetString());
        if (needsReplacement && !hasReplacement)
            return $"Style Guide termRules.{kind} requires a non-empty replacement.";
        if (!needsReplacement && kind != "capitalize"
            && rule.TryGetProperty("replacement", out var unused)
            && unused.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            && !(unused.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(unused.GetString())))
        {
            return $"Style Guide termRules.{kind} must not set replacement.";
        }
        return null;
    }

    private static HashSet<string> CollectPhrases(JsonElement payload, string mode)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listName = mode == "prohibit" ? "prohibitedPhrases" : "requiredPhrases";
        if (payload.TryGetProperty(listName, out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    values.Add(item.GetString()!.Trim());
            }
        }
        if (payload.TryGetProperty("termRules", out var termRules) && termRules.ValueKind == JsonValueKind.Array)
        {
            foreach (var rule in termRules.EnumerateArray())
            {
                if (!rule.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
                    continue;
                var kindValue = kind.GetString();
                if (mode == "prohibit" && !string.Equals(kindValue, "prohibit", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (mode == "require")
                    continue;
                if (rule.TryGetProperty("match", out var match)
                    && match.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(match.GetString()))
                {
                    values.Add(match.GetString()!.Trim());
                }
            }
        }
        return values;
    }
}
