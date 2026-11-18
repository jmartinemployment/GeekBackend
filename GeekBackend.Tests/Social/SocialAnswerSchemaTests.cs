using System.Text.Json;
using GeekAPI.Services.Social.Drafting;
using GeekAPI.Services.Social.Publishing;
using GeekAPI.Services.Social.Triage;
using GeekApplication.Models.Social;

namespace GeekBackend.Tests.Social;

/// <summary>
/// Every answer schema GeekSocial sends is held to the strictest rules any provider applies, so each
/// provider enforces it natively and none has to loosen it. OpenAI's strict mode sets the bar: an
/// object, every property listed as required, no additional properties. Beyond that the schemas stay
/// flat (no nested object or array, no null type, no union of types) and use only the keywords every
/// provider reads (type, description, enum), so Gemini's and Perplexity's schema support take them as
/// written. A length or a count is checked in code after the answer comes back, not in the schema.
/// </summary>
public class SocialAnswerSchemaTests
{
    private static readonly HashSet<string> RootKeywords = new(StringComparer.Ordinal) { "type", "properties", "required", "additionalProperties" };
    private static readonly HashSet<string> PropertyKeywords = new(StringComparer.Ordinal) { "type", "description", "enum" };
    private static readonly HashSet<string> PropertyTypes = new(StringComparer.Ordinal) { "string", "number", "integer", "boolean" };

    public static TheoryData<string, string> SchemasInUse()
    {
        var data = new TheoryData<string, string>
        {
            { "triage", EngagementTriage.AnswerSchema },
            { "reply", ReplyDrafter.AnswerSchema },
            { "posts for every platform", PostWriter.AnswerSchema(SocialPlatforms.All.ToList()) },
        };
        foreach (var platform in SocialPlatforms.All)
            data.Add($"post for {platform}", PostWriter.AnswerSchema(new[] { platform }));
        return data;
    }

    [Theory]
    [MemberData(nameof(SchemasInUse))]
    public void Each_schema_in_use_is_flat_strict_and_minimal(string name, string schemaJson)
    {
        using var doc = JsonDocument.Parse(schemaJson);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.All(root.EnumerateObject(), p => Assert.True(RootKeywords.Contains(p.Name), $"{name}: '{p.Name}' is not a keyword every provider reads at the root."));
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.False, root.GetProperty("additionalProperties").ValueKind);

        var properties = root.GetProperty("properties").EnumerateObject().ToList();
        Assert.NotEmpty(properties);
        var required = root.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(properties.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal), required.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(required.Count, required.Distinct().Count());

        foreach (var property in properties)
        {
            var where = $"{name}.{property.Name}";
            Assert.Equal(JsonValueKind.Object, property.Value.ValueKind);
            Assert.All(property.Value.EnumerateObject(), k => Assert.True(PropertyKeywords.Contains(k.Name), $"{where}: '{k.Name}' is not type, description or enum."));

            var type = property.Value.GetProperty("type");
            Assert.True(type.ValueKind == JsonValueKind.String, $"{where}: the type is a union or not a string.");
            Assert.True(PropertyTypes.Contains(type.GetString()!), $"{where}: '{type.GetString()}' is not string, number, integer or boolean.");

            if (property.Value.TryGetProperty("enum", out var values))
            {
                Assert.Equal("string", type.GetString());
                Assert.NotEmpty(values.EnumerateArray());
                Assert.All(values.EnumerateArray(), v => Assert.Equal(JsonValueKind.String, v.ValueKind));
            }
        }
    }

    [Fact]
    public void Every_answer_name_is_letters_and_digits_only()
    {
        foreach (var name in new[] { EngagementTriage.AnswerName, ReplyDrafter.AnswerName, PostWriter.AnswerName })
            Assert.True(name.Length is > 0 and <= 64 && name.All(char.IsAsciiLetterOrDigit), name);
    }
}
