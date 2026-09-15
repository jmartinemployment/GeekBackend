using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace GeekAPI.Services.ContentCreatorV2.Generation;

/// <summary>
/// Builds a strict (OpenAI/Groq structured-output compatible) JSON schema for a plain DTO type
/// via .NET's <see cref="JsonSchemaExporter"/> — the exact pattern used by
/// <see cref="GeekAPI.Services.Workflow.Services.ContentSectionJsonSchema"/>, generalized for the
/// metadata/extraction/transform DTOs rerouted onto the schema-constrained generation mechanism
/// (see <see cref="IGccV2SchemaConstrainedGenerator"/>). Every object node gets
/// <c>additionalProperties: false</c> plus a full <c>required</c> list, and <c>oneOf</c> is swapped
/// for <c>anyOf</c> (Groq structured outputs reject <c>oneOf</c>/<c>const</c>) — same requirements
/// as <c>ContentSectionJsonSchema</c>.
///
/// <see cref="Workflow.Domain.Entities.Section"/>-shaped results should keep using
/// <c>ContentSectionJsonSchema.SectionSchema</c> / <c>SectionsArraySchema</c> directly instead of
/// this helper — those already carry the hand-written <c>Paragraph</c> union shape and the
/// root-self-reference rewrite that <see cref="Section.Children"/>'s recursion needs, neither of
/// which this generalized helper attempts to infer.
/// </summary>
public static class GccV2AdHocJsonSchema
{
    private static readonly JsonSchemaExporterOptions ExporterOptions = new()
    {
        TransformSchemaNode = TransformNode,
    };

    public static string For<T>(JsonSerializerOptions serializerOptions)
    {
        var node = JsonSchemaExporter.GetJsonSchemaAsNode(serializerOptions, typeof(T), ExporterOptions);
        return node.ToJsonString();
    }

    private static JsonNode TransformNode(JsonSchemaExporterContext context, JsonNode node)
    {
        _ = context;

        // Drop the "null" branch of a nullable ["object","null"] union at the root/nested object
        // level — every DTO rerouted through this helper is a required, non-null response payload.
        if (node is JsonObject obj0
            && obj0.TryGetPropertyValue("type", out var typeArrNode)
            && typeArrNode is JsonArray { Count: 2 } typeArr
            && typeArr[0]?.ToString() == "object"
            && typeArr[1]?.ToString() == "null")
        {
            obj0["type"] = "object";
        }

        if (node is JsonObject obj
            && obj.TryGetPropertyValue("type", out var typeNode)
            && typeNode is JsonValue typeValue
            && typeValue.GetValue<string>() == "object"
            && obj.TryGetPropertyValue("properties", out var propsNode)
            && propsNode is JsonObject props)
        {
            obj["additionalProperties"] = false;
            obj["required"] = new JsonArray(props.Select(p => JsonValue.Create(p.Key) as JsonNode).ToArray());
        }

        if (node is JsonObject withOneOf && withOneOf.TryGetPropertyValue("oneOf", out var oneOfNode) && oneOfNode is JsonArray)
        {
            withOneOf["anyOf"] = oneOfNode.DeepClone();
            withOneOf.Remove("oneOf");
        }

        if (node is JsonObject withDefault)
        {
            withDefault.Remove("default");
        }

        return node;
    }
}
