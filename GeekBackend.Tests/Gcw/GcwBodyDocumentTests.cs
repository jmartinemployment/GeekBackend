using GeekAPI.Services.Gcw;

namespace GeekBackend.Tests.Gcw;

/// <summary>
/// Reading a stored body. Reported: "SEO score 0 · 0 words · density 0.0%" on a finished draft, with
/// every check failing -- including the ones that only measure length.
///
/// <para>
/// Three faults in the walkers, any one of them enough on its own, and all three the same cause:
/// they were written against a body shape that changed underneath them. These pin the shape the
/// generators actually produce, so a score of 0 means an empty draft again.
/// </para>
/// </summary>
public class GcwBodyDocumentTests
{
    /// <summary>What every long-form type returns: the document inside an envelope.</summary>
    private const string Envelope = """
    {
      "title": "Automated Accounts Payable",
      "metaDescription": "Meta.",
      "summary": "Summary.",
      "body": {
        "lede": {"tag":"h2","heading":"Opening","paragraphs":[
          {"type":"text","runs":[{"text":"Invoices pile up on a Friday."}]}
        ],"href":null,"children":[]},
        "sections": [
          {"tag":"h2","heading":"Where the hours go","paragraphs":[
            {"type":"text","runs":[{"text":"Three days of keying."}]},
            {"type":"list","ordered":false,"items":[[{"text":"Capture"}],[{"text":"Approve"}]]},
            {"type":"quote","runs":[{"text":"We cut it to two."}],"cite":"https://p.test"}
          ],"href":null,"children":[
            {"tag":"h3","heading":"Routing","paragraphs":[
              {"type":"text","runs":[{"text":"Rules decide."}]}
            ],"href":null,"children":[]}
          ]}
        ]
      },
      "jsonLdSchema": "{}"
    }
    """;

    [Fact]
    public void The_envelope_is_unwrapped()
    {
        // The walkers read lede/sections off the root, which on an envelope holds neither, so every
        // long-form draft measured as empty.
        var text = GcwBodyDocument.Read(Envelope);

        Assert.Equal(1, text.SectionCount);
        Assert.Contains("Three days of keying.", text.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_lede_is_a_section_not_a_string()
    {
        // Read as a string it was skipped entirely, so "keyword in lede" could never pass.
        var text = GcwBodyDocument.Read(Envelope);

        Assert.Equal("Invoices pile up on a Friday.", text.Lede);
    }

    [Fact]
    public void Paragraph_runs_are_collected_because_the_converter_writes_type_not_dollar_type()
    {
        var text = GcwBodyDocument.Read(Envelope);

        Assert.Contains("Three days of keying.", text.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void List_items_and_quotes_count_as_words_on_the_page()
    {
        // A draft is not shorter for having quoted someone, or for making a point in a list.
        var text = GcwBodyDocument.Read(Envelope);

        Assert.Contains("Capture", text.PlainText, StringComparison.Ordinal);
        Assert.Contains("We cut it to two.", text.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_subsections_are_walked()
    {
        var text = GcwBodyDocument.Read(Envelope);

        Assert.Contains("Routing", text.Headings);
        Assert.Contains("Rules decide.", text.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ledes_own_heading_is_counted_as_a_heading()
    {
        // Inverted 2026-09-29. This asserted the opposite, reasoning that the lede has no heading on
        // the page -- true of a headingless lede, and the lede is not one (Jeff): it is the page's
        // first H2 and SectionHtmlRenderer emits the tag. Excluding the heading most likely to carry
        // the keyword made a draft fail "keyword in a heading" on text the reader does see as one.
        var text = GcwBodyDocument.Read(Envelope);

        Assert.Contains("Opening", text.Headings);
        Assert.Contains("Where the hours go", text.Headings);
    }

    [Fact]
    public void A_lede_with_no_heading_contributes_none()
    {
        // No synthesis and no fallback: a lede the model returned without a heading adds nothing to
        // the heading list, rather than borrowing the title or the first section's.
        const string headingless = """
        {"lede":{"tag":"h2","heading":"","paragraphs":[{"type":"text","runs":[{"text":"Opens."}]}],"href":null,"children":[]},
         "sections":[{"tag":"h2","heading":"One","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}],"href":null,"children":[]}]}
        """;

        var text = GcwBodyDocument.Read(headingless);

        Assert.Equal(["One"], text.Headings);
    }

    [Fact]
    public void A_bare_document_still_reads()
    {
        // Older artifacts stored the document without the envelope.
        const string bare = """
        {"lede":{"tag":"h2","heading":"Opening","paragraphs":[{"type":"text","runs":[{"text":"Bare."}]}],"href":null,"children":[]},
         "sections":[{"tag":"h2","heading":"One","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}],"href":null,"children":[]}]}
        """;

        var text = GcwBodyDocument.Read(bare);

        Assert.Equal("Bare.", text.Lede);
        Assert.Equal(1, text.SectionCount);
    }

    [Fact]
    public void A_body_that_will_not_parse_reads_as_empty_not_as_its_own_json()
    {
        // Returning the raw JSON scored a broken body as a long draft full of braces.
        var text = GcwBodyDocument.Read("{ not json");

        Assert.Equal("", text.PlainText);
        Assert.Equal(0, text.SectionCount);
        Assert.Empty(text.Headings);
    }
}
