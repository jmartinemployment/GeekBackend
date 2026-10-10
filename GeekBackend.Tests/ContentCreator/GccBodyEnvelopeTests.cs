using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The envelope a stored body is wrapped in.
///
/// <para>
/// Deserializing the stored string straight into a ContentDocument does not fail on an envelope --
/// System.Text.Json does not enforce a record's non-nullable parameters, so it returns a document with
/// a null Lede, the null check passes, and the next dereference throws. The export button went down
/// that way in September.
/// </para>
/// </summary>
public class GccBodyEnvelopeTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ParagraphJsonConverter() },
    };

    private static ContentDocument Document(string ledeText) => new(
        new Section("h2", "Opening", [new TextParagraph([new Run(ledeText)])], null, []),
        [new Section("h2", "One", [new TextParagraph([new Run("Body.")])], null, [])]);

    private static string Envelope(string ledeText) => JsonSerializer.Serialize(new
    {
        title = "Automated Accounts Payable",
        metaDescription = "Meta.",
        summary = "Summary.",
        body = Document(ledeText),
        jsonLdSchema = "{\"@type\":\"BlogPosting\"}",
    }, Options);

    [Fact]
    public void An_envelope_yields_the_document_inside_it()
    {
        var parsed = GccBodyEnvelope.Read(Envelope("Invoices pile up."), Options);

        Assert.NotNull(parsed.Document);
        Assert.Equal("Opening", parsed.Document!.Lede.Heading);
        Assert.Equal("Automated Accounts Payable", parsed.Title);
        Assert.Equal("{\"@type\":\"BlogPosting\"}", parsed.JsonLdSchema);
    }

    [Fact]
    public void A_bare_document_still_reads_and_carries_no_envelope()
    {
        var parsed = GccBodyEnvelope.Read(JsonSerializer.Serialize(Document("Bare."), Options), Options);

        Assert.NotNull(parsed.Document);
        Assert.Null(parsed.Title);
    }

    [Fact]
    public void A_body_that_is_not_a_document_reads_as_null_rather_than_an_empty_shell()
    {
        // The exact failure: this used to deserialize into a ContentDocument with a null Lede,
        // survive the null check, and throw on first use.
        var parsed = GccBodyEnvelope.Read("""{"prompts":[{"heading":"Hero","prompt":"A desk."}]}""", Options);

        Assert.Null(parsed.Document);
    }

    [Fact]
    public void Unparseable_json_reads_as_null()
    {
        Assert.Null(GccBodyEnvelope.Read("{ not json", Options).Document);
        Assert.Null(GccBodyEnvelope.Read(null, Options).Document);
        Assert.Null(GccBodyEnvelope.Read("   ", Options).Document);
    }
}
