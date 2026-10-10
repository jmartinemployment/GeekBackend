using System.Text.Json;
using System.Text.Json.Serialization;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>
/// Writes a timestamp the way Geek-Crawler-Rag's Python side does: UTC, a trailing <c>Z</c>, and only
/// as many fractional digits as the value has. The two sides hash the same JSON, so the text has to match.
/// </summary>
public sealed class PythonDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        var utc = value.ToUniversalTime();
        var fractional = utc.ToString("fffffff").TrimEnd('0');
        writer.WriteStringValue(fractional.Length == 0
            ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            : utc.ToString("yyyy-MM-dd'T'HH:mm:ss") + "." + fractional + "Z");
    }
}

/// <summary>Writes a double the way Python's <c>json</c> does: always with a decimal point.</summary>
public sealed class PythonDoubleConverter : JsonConverter<double>
{
    public override double Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDouble();

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("0.0################",
            System.Globalization.CultureInfo.InvariantCulture));
}
