using System.Text.Json;
using GeekApplication.Entities.OpenIddict;

namespace GeekRepository.OpenIddict;

internal static class OpenIddictJson
{
    public static string? SerializeStringArray(IEnumerable<string>? values) =>
        values is null ? null : JsonSerializer.Serialize(values);

    public static string? SerializeDictionary(IDictionary<string, string>? values) =>
        values is null ? null : JsonSerializer.Serialize(values);

    public static IReadOnlyList<string> DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    public static IReadOnlyDictionary<string, string> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>();
    }

    public static bool JsonArrayContains(string? json, string value)
    {
        foreach (var item in DeserializeStringArray(json))
        {
            if (string.Equals(item, value, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
