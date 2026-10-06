using System.Text.Json.Nodes;
using GeekAPI.Services.Workflow.Services;

namespace GeekBackend.Tests.Workflow;

/// <summary>
/// Strict structured output lists every property as required, so a field the model has nothing to
/// put in must be able to say null. A non-nullable <c>provenance</c> or <c>imagePrompt</c> would force
/// the model to invent a value on every call that does not use one (tool pages, the FAQ, the
/// opening), and the static system contract tells it to leave them null.
/// </summary>
public class SectionSchemaNullabilityTests
{
    private static JsonObject SectionDefinition()
    {
        var schema = JsonNode.Parse(ContentSectionJsonSchema.SectionsArraySchema)!;
        return (JsonObject)schema["$defs"]!["section"]!;
    }

    [Theory]
    [InlineData("provenance")]
    [InlineData("imagePrompt")]
    [InlineData("href")]
    public void An_optional_section_field_is_required_and_may_be_null(string field)
    {
        var section = SectionDefinition();

        var required = ((JsonArray)section["required"]!).Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains(field, required);

        var property = section["properties"]![field];
        Assert.NotNull(property);
        Assert.True(AllowsNull(property!), $"'{field}' is required by strict mode but cannot be null: {property}");
    }

    private static bool AllowsNull(JsonNode property)
    {
        if (property["type"] is JsonArray types)
        {
            return types.Any(t => t?.GetValue<string>() == "null");
        }

        if (property["anyOf"] is JsonArray branches)
        {
            return branches.Any(b => b?["type"]?.ToString() == "null");
        }

        return false;
    }
}
