using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeekApplication.Models.Crm;

// Custom properties and the industry lookup (docs/GEEK-CRM-DATA-MODEL.md, 4.10 and 4.11). A tenant
// defines a property once, then sets a value on any person, organization or deal. The XSD describes
// the two structures; each tenant's own keys and values are checked here, against its definitions.

public sealed record CrmPropertyDefinition(
    Guid Id,
    string ObjectType,
    string Key,
    string Label,
    string DataType,
    IReadOnlyList<string>? Options,
    bool IsSensitive,
    DateTime? RetiredAt,
    DateTime CreatedAt);

/// <summary>The object type, key and data type never change once a property exists; a different one is a new property.</summary>
public sealed record CreatePropertyDefinitionCommand(
    Guid TenantId,
    string ObjectType,
    string Key,
    string Label,
    string DataType,
    IReadOnlyList<string>? Options,
    bool IsSensitive);

public sealed record UpdatePropertyDefinitionCommand(
    Guid TenantId,
    string Label,
    IReadOnlyList<string>? Options,
    bool IsSensitive);

/// <summary>A value as the API returns it: the definition's key and label, and the value as JSON.</summary>
public sealed record CrmPropertyValue(
    Guid DefinitionId,
    string Key,
    string Label,
    string DataType,
    bool IsSensitive,
    JsonElement Value);

/// <summary>
/// Values to set on one record, by property key. A JSON null clears the value. Every value is checked
/// before any is written, and all are written in one transaction or none are.
/// <see cref="IncludeSensitive"/> is whether the caller may see and set sensitive properties; a
/// sensitive property the caller may not see is answered as an unknown key.
/// </summary>
public sealed record SetPropertyValuesCommand(
    Guid TenantId,
    string ObjectType,
    Guid RecordId,
    IReadOnlyDictionary<string, JsonElement> Values,
    bool IncludeSensitive);

public sealed record CrmNaicsCode(string Code, string Title, int Level);

/// <summary>What a property definition or a value must be. A null result is valid; a string is what is wrong, in words for a person.</summary>
public static partial class CrmPropertyRules
{
    public const int MaxLabelLength = 120;
    public const int MaxTextLength = 4000;
    public const int MaxOptions = 200;
    public const int MaxOptionLength = 100;

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,62}$", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex KeyRegex();

    public static bool IsKey(string? value) => value is not null && KeyRegex().IsMatch(value);

    public static bool UsesOptions(string dataType) => dataType is PropertyDataTypes.Enum or PropertyDataTypes.MultiEnum;

    public static string? ValidateDefinition(string objectType, string key, string label, string dataType, IReadOnlyList<string>? options, bool checkIdentity = true)
    {
        if (checkIdentity)
        {
            if (!PropertyObjectTypes.All.Contains(objectType))
                return $"Unknown object type '{objectType}'. Use person, organization or deal.";
            if (!IsKey(key))
                return "A key is lower case letters, digits and underscores, starts with a letter, and is at most 63 characters.";
            if (!PropertyDataTypes.All.Contains(dataType))
                return $"Unknown data type '{dataType}'.";
        }
        if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > MaxLabelLength)
            return $"A label is required and at most {MaxLabelLength} characters.";
        return ValidateOptions(dataType, options);
    }

    public static string? ValidateOptions(string dataType, IReadOnlyList<string>? options)
    {
        if (!UsesOptions(dataType))
            return options is { Count: > 0 } ? $"A {dataType} property has no options." : null;
        if (options is null || options.Count == 0)
            return $"A {dataType} property needs its options.";
        if (options.Count > MaxOptions)
            return $"At most {MaxOptions} options.";
        if (options.Any(o => string.IsNullOrWhiteSpace(o) || o.Trim().Length > MaxOptionLength))
            return $"Every option is required and at most {MaxOptionLength} characters.";
        if (options.Select(o => o.Trim()).Distinct(StringComparer.Ordinal).Count() != options.Count)
            return "Options must be different from each other.";
        return null;
    }

    /// <summary>Checks one value against its definition's data type and options.</summary>
    public static string? ValidateValue(string dataType, IReadOnlyList<string>? options, JsonElement value)
    {
        switch (dataType)
        {
            case PropertyDataTypes.Text:
                return value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= MaxTextLength
                    ? null
                    : $"Expected text of at most {MaxTextLength} characters.";
            case PropertyDataTypes.Number:
                return value.ValueKind == JsonValueKind.Number ? null : "Expected a number.";
            case PropertyDataTypes.Date:
                return value.ValueKind == JsonValueKind.String
                       && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? null
                    : "Expected a date as yyyy-MM-dd.";
            case PropertyDataTypes.Bool:
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "Expected true or false.";
            case PropertyDataTypes.Enum:
                return value.ValueKind == JsonValueKind.String && options is not null && options.Contains(value.GetString()!)
                    ? null
                    : "Expected one of the property's options.";
            case PropertyDataTypes.MultiEnum:
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0 || options is null)
                    return "Expected a list of the property's options, at least one.";
                var chosen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || !options.Contains(item.GetString()!))
                        return "Every choice must be one of the property's options.";
                    if (!chosen.Add(item.GetString()!))
                        return "A choice is listed twice.";
                }
                return null;
            default:
                return $"Unknown data type '{dataType}'.";
        }
    }
}
