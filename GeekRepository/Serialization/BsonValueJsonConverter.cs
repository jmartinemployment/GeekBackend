using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;

namespace GeekRepository.Serialization;

/// <summary>
/// Writes a <see cref="BsonValue"/> as plain JSON.
///
/// Without this, System.Text.Json serialises a BsonArray by reflecting over its public properties.
/// <see cref="BsonValue"/> exposes dozens of typed accessors — <c>AsString</c>, <c>AsInt32</c>,
/// <c>AsBoolean</c> and the rest — and every one of them throws when the underlying value is not
/// that type. Serialisation then dies part-way through the response, after a 200 and a partial body
/// have already been written, and the caller receives well-formed headers wrapped around truncated
/// JSON. That is how crawl pages carrying typed blocks turned into
/// "Expected depth to be zero at the end of the JSON payload" on the other side.
///
/// Read is not supported: these documents are written through the Mongo driver, never parsed back
/// from JSON here. A converter that silently accepted input it cannot faithfully round-trip would be
/// worse than one that refuses.
/// </summary>
public sealed class BsonValueJsonConverter : JsonConverter<BsonValue>
{
    public override bool CanConvert(Type typeToConvert) => typeof(BsonValue).IsAssignableFrom(typeToConvert);

    public override BsonValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("BsonValue is written, never read, by this service.");

    public override void Write(Utf8JsonWriter writer, BsonValue value, JsonSerializerOptions options) =>
        WriteValue(writer, value);

    private static void WriteValue(Utf8JsonWriter writer, BsonValue value)
    {
        switch (value.BsonType)
        {
            case BsonType.Document:
                writer.WriteStartObject();
                foreach (var element in value.AsBsonDocument)
                {
                    writer.WritePropertyName(element.Name);
                    WriteValue(writer, element.Value);
                }
                writer.WriteEndObject();
                break;

            case BsonType.Array:
                writer.WriteStartArray();
                foreach (var item in value.AsBsonArray)
                    WriteValue(writer, item);
                writer.WriteEndArray();
                break;

            case BsonType.String:
                writer.WriteStringValue(value.AsString);
                break;

            case BsonType.Boolean:
                writer.WriteBooleanValue(value.AsBoolean);
                break;

            case BsonType.Int32:
                writer.WriteNumberValue(value.AsInt32);
                break;

            case BsonType.Int64:
                writer.WriteNumberValue(value.AsInt64);
                break;

            case BsonType.Double:
                writer.WriteNumberValue(value.AsDouble);
                break;

            case BsonType.Decimal128:
                writer.WriteNumberValue(value.AsDecimal);
                break;

            case BsonType.DateTime:
                writer.WriteStringValue(value.ToUniversalTime());
                break;

            case BsonType.ObjectId:
                writer.WriteStringValue(value.AsObjectId.ToString());
                break;

            case BsonType.Null:
            case BsonType.Undefined:
                writer.WriteNullValue();
                break;

            default:
                // Binary, RegularExpression, JavaScript and the rest. These do not appear in crawl
                // blocks; if one ever does, its string form is honest and the caller can see it
                // rather than the response failing mid-flight.
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
