using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using Xunit;

namespace GeekBackend.Tests.Workflow;

/// <summary>
/// A <see cref="ContentDocument"/> must be readable and writable with <i>any</i>
/// <see cref="JsonSerializerOptions"/>, because <see cref="Paragraph"/> declares its own converter.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure these exist for.</b> <c>Paragraph</c> is abstract, so System.Text.Json refuses it
/// outright without the converter: <i>"Deserialization of interface or abstract types is not
/// supported. Type 'Paragraph'"</i>. That is a <c>NotSupportedException</c>, not a
/// <c>JsonException</c> — so <c>GccBodyEnvelope.Read</c>'s <c>catch (JsonException)</c>, which exists
/// to export an unreadable body verbatim rather than drop it, does not catch it. It escaped as an
/// unhandled 500.
/// </para>
/// <para>
/// The converter was registered by hand at eleven call sites. <c>GccArtifactExportService</c> was the
/// twelfth and used a bare <c>new(JsonSerializerDefaults.Web)</c>, so <b>Export HTML threw the first
/// time a create had real artifacts to export</b> (2026-10-02) — the feature was only ever exercised
/// against creates with nothing in them.
/// </para>
/// <para>
/// So these tests deliberately use options that register <b>nothing</b>. Passing options that add the
/// converter would restate what the eleven call sites already do and would not have caught this.
/// </para>
/// </remarks>
public class ParagraphConverterTravelsWithTheTypeTests
{
    /// <summary>Registers nothing. The whole point: this is what a careless new caller passes.</summary>
    private static JsonSerializerOptions BareOptions => new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_stored_document_reads_with_options_that_register_nothing()
    {
        const string stored = """
            {"lede":{"tag":"h1","heading":"H","paragraphs":[{"type":"text","runs":[{"text":"hello"}]}],"href":null,"children":[]},"sections":[]}
            """;

        var document = JsonSerializer.Deserialize<ContentDocument>(stored, BareOptions);

        Assert.NotNull(document);
        var paragraph = Assert.Single(document!.Lede!.Paragraphs);
        var text = Assert.IsType<TextParagraph>(paragraph);
        Assert.Equal("hello", Assert.Single(text.Runs).Text);
    }

    [Fact]
    public void The_export_reads_a_stored_envelope_rather_than_throwing()
    {
        // The exact shape and the exact call GccArtifactExportService makes.
        const string stored = """
            {"title":"Dext","metaDescription":"d","summary":"s","jsonLdSchema":"{}",
             "body":{"lede":{"tag":"h1","heading":"Dext","paragraphs":[{"type":"quote","cite":"https://dext.com/x","runs":[{"text":"We capture every line item."}]}],"href":null,"children":[]},"sections":[]}}
            """;

        var parsed = GccBodyEnvelope.Read(stored, BareOptions);

        Assert.NotNull(parsed.Document);
        Assert.Equal("Dext", parsed.Title);
        var quote = Assert.IsType<QuoteParagraph>(Assert.Single(parsed.Document!.Lede!.Paragraphs));
        Assert.Equal("https://dext.com/x", quote.Cite);
    }

    /// <summary>Every concrete <see cref="Paragraph"/>. A new subtype belongs here — the checklist on
    /// <c>Paragraph</c> says so, and this is the test that enforces the serialization half of it.</summary>
    private static readonly Paragraph[] AllSubtypes =
    [
        new TextParagraph([new Run("plain"), new Run("bold", Bold: true)]),
        new ListParagraph(true, [[new Run("one")], [new Run("two")]]),
        new QuoteParagraph([new Run("quoted")], "https://partner.test/page"),
        new CodeParagraph("curl https://partner.test/api", "bash"),
        new DefinitionParagraph([new DefinitionItem([new Run("AP")], [new Run("Accounts payable")])]),
    ];

    public static TheoryData<Paragraph> EverySubtype
    {
        get
        {
            var data = new TheoryData<Paragraph>();
            foreach (var paragraph in AllSubtypes) data.Add(paragraph);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EverySubtype))]
    public void Every_subtype_round_trips_through_bare_options(Paragraph original)
    {
        // Not just "does not throw": a subtype missing from Write serializes to "{}" and the content is
        // gone with no error, which is exactly how QuoteParagraph was lost before 2026-09-23. Code and
        // definition had the same gap until the converter was declared on the type, at which point every
        // serialization anywhere started going through this switch.
        var json = JsonSerializer.Serialize(original, BareOptions);
        var back = JsonSerializer.Deserialize<Paragraph>(json, BareOptions);

        Assert.NotEqual("{}", json);
        Assert.Equal(original.GetType(), back!.GetType());
        // Re-serialized rather than compared with Assert.Equal: a record's generated equality uses
        // EqualityComparer<T>.Default for its members, so two structurally identical paragraphs whose
        // Runs are different list instances are never equal. Comparing the wire form compares the
        // content, which is what must survive.
        Assert.Equal(json, JsonSerializer.Serialize(back, BareOptions));
    }

    [Fact]
    public void A_document_round_trips_with_every_subtype_in_it()
    {
        var document = new ContentDocument(
            new Section("h1", "Dext", AllSubtypes, null, []),
            []);

        var json = JsonSerializer.Serialize(document, BareOptions);
        var back = JsonSerializer.Deserialize<ContentDocument>(json, BareOptions);

        Assert.Equal(AllSubtypes.Length, back!.Lede!.Paragraphs.Count);
        Assert.Equal(
            AllSubtypes.Select(para => para.GetType()),
            back.Lede.Paragraphs.Select(para => para.GetType()));
        Assert.Equal(json, JsonSerializer.Serialize(back, BareOptions));
    }
}
