using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace GeekAPI.HttpClients.OpenIddict;

internal static class OpenIddictStoreJson
{
    public static ImmutableArray<string> ReadStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ImmutableArray<string>.Empty;
        var list = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        return [.. list];
    }

    public static string WriteStringArray(ImmutableArray<string> values) =>
        JsonSerializer.Serialize(values.ToArray());

    public static ImmutableDictionary<string, string> ReadDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ImmutableDictionary<string, string>.Empty;
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        return dict.ToImmutableDictionary();
    }

    public static string WriteDictionary(ImmutableDictionary<string, string> values) =>
        JsonSerializer.Serialize(values.ToDictionary(static x => x.Key, static x => x.Value));

    public static ImmutableDictionary<CultureInfo, string> ReadCultureDictionary(string? json) =>
        ReadDictionary(json).ToImmutableDictionary(static x => CultureInfo.GetCultureInfo(x.Key), static x => x.Value);

    public static string WriteCultureDictionary(ImmutableDictionary<CultureInfo, string> values) =>
        WriteDictionary(values.ToImmutableDictionary(static x => x.Key.Name, static x => x.Value));

    public static ImmutableDictionary<string, JsonElement> ReadProperties(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ImmutableDictionary<string, JsonElement>.Empty;
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
            ?? new Dictionary<string, JsonElement>();
        return dict.ToImmutableDictionary();
    }

    public static string WriteProperties(ImmutableDictionary<string, JsonElement> properties) =>
        JsonSerializer.Serialize(properties.ToDictionary(static x => x.Key, static x => x.Value));
}
