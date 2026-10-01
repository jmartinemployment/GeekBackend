using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekBackend.Tests.Workflow;

/// <summary>
/// The schema the provider enforces and the converter that reads the response are one wire shape in
/// two places. These assert they agree.
/// </summary>
/// <remarks>
/// <para>
/// They did not. <c>ParagraphJsonConverter</c> learned to read <c>"type":"quote"</c> on 2026-09-23,
/// and <c>ContentSectionJsonSchema</c> was never taught to permit emitting one — so the tool page
/// asked for a block quotation, listed the spans it could use, and refused the result for not carrying
/// one, while the model had no legal shape to answer with. A real generate reported
/// <i>"40 quotable partner span(s) were supplied and none was used"</i>.
/// </para>
/// <para>
/// Nothing caught it because no test compared the two. The unit tests hand fixtures straight to a fake
/// provider, which ignores the schema, so <c>"type":"quote"</c> parsed happily in every test and was
/// impossible in production. A drift test is the only kind that would have.
/// </para>
/// </remarks>
public class ParagraphWireShapeTests
{
    private static JsonArray Variants()
    {
        var schema = JsonNode.Parse(ContentSectionJsonSchema.SectionsArraySchema)!;
        var found = FindParagraphUnion(schema);
        Assert.NotNull(found);
        return (JsonArray)found!["anyOf"]!;
    }

    /// <summary>The union is injected as a paragraph array's "items"; find it by its discriminators.</summary>
    private static JsonObject? FindParagraphUnion(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["anyOf"] is JsonArray branches
                    && branches.Any(b => Discriminator(b) == "text")
                    && branches.Any(b => Discriminator(b) == "list"))
                {
                    return obj;
                }

                foreach (var (_, value) in obj)
                {
                    if (FindParagraphUnion(value) is { } hit) return hit;
                }

                return null;

            case JsonArray arr:
                foreach (var item in arr)
                {
                    if (FindParagraphUnion(item) is { } hit) return hit;
                }

                return null;

            default:
                return null;
        }
    }

    private static string? Discriminator(JsonNode? branch) =>
        branch?["properties"]?["type"]?["enum"] is JsonArray e && e.Count > 0 ? e[0]!.GetValue<string>() : null;

    [Theory]
    [InlineData("text")]
    [InlineData("list")]
    // The one that was missing. A tool page cannot be written without it.
    [InlineData("quote")]
    public void The_schema_permits_every_shape_the_converter_reads(string discriminator)
    {
        Assert.Contains(Variants(), branch => Discriminator(branch) == discriminator);
    }

    [Fact]
    public void A_quote_carries_its_source_so_the_cite_is_never_the_models_prose()
    {
        var quote = Assert.Single(Variants(), b => Discriminator(b) == "quote")!;

        // Strict mode requires every declared property to be required, so cite is a nullable string
        // rather than an omitted one -- the same way Run.href is expressed.
        var required = Assert.IsType<JsonArray>(quote["required"]);
        Assert.Contains("cite", required.Select(r => r!.GetValue<string>()));
        Assert.Contains("runs", required.Select(r => r!.GetValue<string>()));
        Assert.False(quote["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void A_quote_the_schema_permits_round_trips_through_the_converter()
    {
        // The two halves meeting: a payload shaped exactly as the schema allows, read by the converter
        // that the provider's response goes through, arriving as the node the renderer emits
        // <blockquote cite="..."> from.
        const string wire =
            """
            {"type":"quote","runs":[{"text":"We cut approval time from nine days to two.","bold":false,"italic":false,"href":null}],"cite":"https://partner.test/customers"}
            """;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ParagraphJsonConverter());

        var paragraph = JsonSerializer.Deserialize<Paragraph>(wire, options);

        var quote = Assert.IsType<QuoteParagraph>(paragraph);
        Assert.Equal("https://partner.test/customers", quote.Cite);
        Assert.Equal("We cut approval time from nine days to two.", Assert.Single(quote.Runs).Text);
    }
}
