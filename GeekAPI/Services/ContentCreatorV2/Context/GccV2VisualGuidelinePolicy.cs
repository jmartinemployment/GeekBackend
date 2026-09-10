using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Typed Visual Guidelines policy (layout/color/logo/usage rules) for Geek IQ.
/// Separate from Style Guide (grammar/terms) and Brand Voice (tone).
/// </summary>
public static class GccV2VisualGuidelinePolicy
{
    public const int CurrentSchemaVersion = 1;

    public static string? Validate(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return "Visual Guidelines policy payload is required.";
        if (payload.ValueKind != JsonValueKind.Object)
            return "Visual Guidelines policy must be a JSON object.";

        if (payload.TryGetProperty("schemaVersion", out var schemaVersion))
        {
            if (schemaVersion.ValueKind != JsonValueKind.Number
                || !schemaVersion.TryGetInt32(out var version)
                || version < 1
                || version > CurrentSchemaVersion)
            {
                return $"Visual Guidelines schemaVersion must be an integer from 1 to {CurrentSchemaVersion}.";
            }
        }

        if (payload.TryGetProperty("palette", out var palette) && palette.ValueKind != JsonValueKind.Null)
        {
            if (palette.ValueKind != JsonValueKind.Object)
                return "Visual Guidelines palette must be an object.";
            foreach (var property in palette.EnumerateObject())
            {
                if (property.Name is not (
                    "primary" or "secondary" or "accent" or "background" or "text"))
                {
                    return $"Unknown Visual Guidelines palette key: {property.Name}.";
                }
                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return $"Visual Guidelines palette.{property.Name} must be a string or null.";
                if (property.Value.ValueKind == JsonValueKind.String
                    && string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return $"Visual Guidelines palette.{property.Name} must be a non-empty string when set.";
                }
            }
        }

        if (payload.TryGetProperty("typography", out var typography) && typography.ValueKind != JsonValueKind.Null)
        {
            if (typography.ValueKind != JsonValueKind.Object)
                return "Visual Guidelines typography must be an object.";
            foreach (var property in typography.EnumerateObject())
            {
                if (property.Name is not ("displayFont" or "bodyFont" or "minBodySizePx"))
                    return $"Unknown Visual Guidelines typography key: {property.Name}.";
                if (property.Name == "minBodySizePx")
                {
                    if (property.Value.ValueKind is not (
                        JsonValueKind.Number or JsonValueKind.Null))
                    {
                        return "Visual Guidelines typography.minBodySizePx must be a number or null.";
                    }
                    if (property.Value.ValueKind == JsonValueKind.Number
                        && (!property.Value.TryGetDouble(out var size) || size < 8 || size > 72))
                    {
                        return "Visual Guidelines typography.minBodySizePx must be between 8 and 72.";
                    }
                }
                else if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                {
                    return $"Visual Guidelines typography.{property.Name} must be a string or null.";
                }
            }
        }

        if (payload.TryGetProperty("logoUsage", out var logo) && logo.ValueKind != JsonValueKind.Null)
        {
            if (logo.ValueKind != JsonValueKind.Object)
                return "Visual Guidelines logoUsage must be an object.";
            foreach (var property in logo.EnumerateObject())
            {
                if (property.Name is not (
                    "clearSpaceRatio" or "allowedBackgrounds" or "prohibitedTreatments"))
                {
                    return $"Unknown Visual Guidelines logoUsage key: {property.Name}.";
                }
                if (property.Name == "clearSpaceRatio")
                {
                    if (property.Value.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
                        return "Visual Guidelines logoUsage.clearSpaceRatio must be a number or null.";
                    if (property.Value.ValueKind == JsonValueKind.Number
                        && (!property.Value.TryGetDouble(out var ratio) || ratio < 0 || ratio > 4))
                    {
                        return "Visual Guidelines logoUsage.clearSpaceRatio must be between 0 and 4.";
                    }
                }
                else
                {
                    var listError = ValidateStringList(property.Value, $"logoUsage.{property.Name}");
                    if (listError is not null) return listError;
                }
            }
        }

        if (payload.TryGetProperty("layout", out var layout) && layout.ValueKind != JsonValueKind.Null)
        {
            if (layout.ValueKind != JsonValueKind.Object)
                return "Visual Guidelines layout must be an object.";
            foreach (var property in layout.EnumerateObject())
            {
                if (property.Name is not (
                    "maxContentWidthPx" or "preferFullBleedHero" or "cornerRadiusPx"))
                {
                    return $"Unknown Visual Guidelines layout key: {property.Name}.";
                }
                if (property.Name == "preferFullBleedHero")
                {
                    if (property.Value.ValueKind is not (
                        JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
                    {
                        return "Visual Guidelines layout.preferFullBleedHero must be a boolean or null.";
                    }
                }
                else if (property.Value.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null))
                {
                    return $"Visual Guidelines layout.{property.Name} must be a number or null.";
                }
            }
        }

        if (payload.TryGetProperty("imagery", out var imagery) && imagery.ValueKind != JsonValueKind.Null)
        {
            if (imagery.ValueKind != JsonValueKind.Object)
                return "Visual Guidelines imagery must be an object.";
            foreach (var property in imagery.EnumerateObject())
            {
                if (property.Name is not ("styleNotes" or "prohibitedMotifs"))
                    return $"Unknown Visual Guidelines imagery key: {property.Name}.";
                if (property.Name == "styleNotes")
                {
                    if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                        return "Visual Guidelines imagery.styleNotes must be a string or null.";
                }
                else
                {
                    var listError = ValidateStringList(property.Value, "imagery.prohibitedMotifs");
                    if (listError is not null) return listError;
                }
            }
        }

        if (payload.TryGetProperty("customInstructions", out var instructions)
            && instructions.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return "Visual Guidelines customInstructions must be a string or null.";
        }

        return null;
    }

    private static string? ValidateStringList(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array)
            return $"Visual Guidelines {path} must be an array of strings.";
        if (value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(x.GetString())))
        {
            return $"Visual Guidelines {path} entries must be non-empty strings.";
        }
        return null;
    }
}
