using System.Text.Json;
using GeekRepository.Serialization;
using MongoDB.Bson;
using Xunit;

namespace GeekBackend.Tests;

/// <summary>
/// Crawl pages carry their typed blocks as a <see cref="BsonArray"/>. Without a converter, System.
/// Text.Json reflects over BsonValue's typed accessors and one of them throws — part-way through
/// the response, after a 200 and a partial body are already written. The caller then sees valid
/// headers wrapped around truncated JSON, which is how a page-list read surfaced as
/// "Expected depth to be zero at the end of the JSON payload".
/// </summary>
public class BsonValueJsonConverterTests
{
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new BsonValueJsonConverter());
        return options;
    }

    private static BsonArray SampleBlocks() =>
    [
        new BsonDocument
        {
            { "kind", "heading" },
            { "level", 1 },
            { "text", "Invoicing" },
            { "anchors", new BsonArray { new BsonDocument { { "text", "Pricing" }, { "href", "/pricing" } } } },
        },
        new BsonDocument { { "kind", "paragraph" }, { "text", "Send invoices." } },
    ];

    [Fact]
    public void Serializing_blocks_without_the_converter_throws()
    {
        // The defect this converter exists for. If this ever stops throwing, the converter may be
        // removable — but do not assume it, check the driver version first.
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Serialize(SampleBlocks()));
    }

    [Fact]
    public void Blocks_serialize_to_plain_json()
    {
        var json = JsonSerializer.Serialize(SampleBlocks(), Options());

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(2, root.GetArrayLength());

        var heading = root[0];
        Assert.Equal("heading", heading.GetProperty("kind").GetString());
        Assert.Equal(1, heading.GetProperty("level").GetInt32());
        Assert.Equal("Invoicing", heading.GetProperty("text").GetString());

        // Heading level and per-block anchors are the whole point: they are what the flat text
        // projection discards, and what site structure is built from.
        var anchors = heading.GetProperty("anchors");
        Assert.Equal(1, anchors.GetArrayLength());
        Assert.Equal("/pricing", anchors[0].GetProperty("href").GetString());
    }

    [Fact]
    public void Scalar_bson_types_keep_their_json_shape()
    {
        var document = new BsonDocument
        {
            { "s", "text" },
            { "b", true },
            { "i", 7 },
            { "l", 9_000_000_000L },
            { "d", 1.5 },
            { "nil", BsonNull.Value },
        };

        var json = JsonSerializer.Serialize(document, Options());
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        Assert.Equal("text", root.GetProperty("s").GetString());
        Assert.True(root.GetProperty("b").GetBoolean());
        Assert.Equal(7, root.GetProperty("i").GetInt32());
        Assert.Equal(9_000_000_000L, root.GetProperty("l").GetInt64());
        Assert.Equal(1.5, root.GetProperty("d").GetDouble());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("nil").ValueKind);
    }
}
