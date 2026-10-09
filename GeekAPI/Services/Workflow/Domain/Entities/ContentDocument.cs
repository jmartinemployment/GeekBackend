using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.Workflow.Domain.Entities;

public enum LedeType
{
    Summary,
    ImmediateIdentification,
    DelayedIdentification,
    SingleItem,
    Anecdotal,
    Narrative,
    SceneSetting,
    StartlingStatement,
    DirectAddress,
    Question,
    Quote,
    Wordplay
}

/// <summary>
/// A span of a paragraph's text. <paramref name="Link"/> is how the writer says these words are a link:
/// the id of a target printed in its prompt (<c>S3</c> a page of the evidence, <c>T2</c> a partner tool
/// page). The writer never writes an address. <c>GccLinkPlacer</c> resolves the id to the page the
/// writer was actually given, puts it in <paramref name="Href"/> and clears <paramref name="Link"/>, so
/// a placed run carries the href and a stored page carries no ids.
/// </summary>
/// <remarks>
/// <para>
/// Until 2026-10-09 the writer typed <c>href</c> directly and chose both the address and the words. It
/// put the address on whole paragraphs (Stampli, ten of 38-75 words; Bill, eight; the pillar, four of
/// 53-64 words) and once on a URL it remembered rather than one it was shown (Ramp).
/// </para>
/// <para>
/// For one day after that the paragraph carried a separate <c>links</c> list of <c>{target, anchor}</c>,
/// the anchor being words copied from the paragraph. The writer paraphrased its own sentence instead
/// of copying it: "real-time dashboards" for a run that began "Real-time dashboards", "Upflow syncs with
/// several software tools" for "Upflow natively syncs with several software tools" -- seven pages
/// refused on one run for words that were not found. Copying a span of your own output into a second
/// field is a recall task, and the model does it approximately. Marking the span where it is written
/// is not: the link is the run, the way bold would be, and there is nothing to match.
/// </para>
/// </remarks>
public sealed record Run(
    string Text,
    bool Bold = false,
    bool Italic = false,
    string? Href = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Link = null);

/// <summary>
/// A block within a section. The set below mirrors the crawler's typed corpus blocks
/// (<c>Geek-Crawler-v2/src/crawl/extract-content.ts</c>): <c>paragraph</c>, <c>listItem</c>,
/// <c>quote</c>, <c>code</c>, <c>term</c>/<c>definition</c>. <c>heading</c> is carried by
/// <see cref="Section"/> rather than by a paragraph type, and anchors by <see cref="Run.Href"/>.
/// </summary>
/// <remarks>
/// <para><b>Adding a subtype means updating every match site, or the content silently vanishes.</b>
/// C# cannot seal this hierarchy, so the checklist lives here:</para>
/// <list type="bullet">
/// <item><c>Workflow/Services/Export/SectionHtmlRenderer.AppendParagraph</c> — the only place tag
/// characters are produced; an unhandled type is dropped from published HTML.</item>
/// <item><c>Workflow/Services/ContentDocumentText.FlattenParagraph</c> — the shared text
/// projection. Do not write a second one.</item>
/// <item><c>ContentCreator/Guardrail/ContentGuardrail.CleanParagraph</c> — an unhandled type
/// bypasses cliché/filler cleaning entirely.</item>
/// <item>Dormant v2 sites, listed so they are not forgotten if that path is revived:
/// <c>ContentCreatorV2/Validate/GccV2AnalyzerDocument</c>,
/// <c>ContentCreatorV2/Validate/GccV2OverlapGate</c>,
/// <c>ContentCreatorV2/Publish/GccV2JsonLdBuilder</c>,
/// <c>ContentCreatorV2/Carousel/GccV2LinkedInCarouselDocumentConverter</c>.</item>
/// </list>
///
/// <para><b>The converter is declared here, not registered per call site.</b> This type is abstract,
/// so <c>System.Text.Json</c> cannot deserialize it at all without
/// <see cref="ParagraphJsonConverter"/> — it throws <c>NotSupportedException: Deserialization of
/// interface or abstract types is not supported</c>. That is not a <c>JsonException</c>, so every
/// <c>catch (JsonException)</c> guarding a document read lets it straight through as an unhandled
/// 500.</para>
///
/// <para>It was registered by hand at eleven call sites instead, and the twelfth —
/// <c>GccArtifactExportService</c>, whose options were a bare
/// <c>new(JsonSerializerDefaults.Web)</c> — took down Export HTML the first time a create had real
/// artifacts to export (2026-10-02). An attribute here cannot be forgotten by a new caller; a
/// registration list can, and did. The explicit registrations still work and still win, since
/// <c>options.Converters</c> takes precedence over an attribute.</para>
/// </remarks>
[JsonConverter(typeof(ParagraphJsonConverter))]
public abstract record Paragraph;

public sealed record TextParagraph(IReadOnlyList<Run> Runs) : Paragraph;

public sealed record ListParagraph(bool Ordered, IReadOnlyList<IReadOnlyList<Run>> Items) : Paragraph;

/// <summary>Corpus <c>quote</c>. <paramref name="Cite"/> is the source URL when the quote came from
/// a retrieved passage — the attribution half of "cite or quote Partners/Tools".</summary>
/// <param name="Candidate">
/// The number of the quotable span this quotation was chosen from, when the writer was shown a
/// numbered list (<c>GccQuoteCandidates</c>). Set, it is the whole answer: the words and the cite are
/// read back from that list by <c>GccToolQuoteGuard.SnapQuotesToCandidates</c>, and whatever the
/// model typed into <paramref name="Runs"/> is discarded. A resolved quotation carries null here --
/// the number is a selector on the way in, not a fact about the page.
/// </param>
public sealed record QuoteParagraph(IReadOnlyList<Run> Runs, string? Cite = null, int? Candidate = null) : Paragraph;

/// <summary>Corpus <c>code</c>. Held as raw text, never runs: bold/italic/href have no meaning
/// inside a code block, and cliché cleaning must not rewrite it.</summary>
public sealed record CodeParagraph(string Code, string? Language = null) : Paragraph;

/// <summary>One <c>term</c>/<c>definition</c> pair from a corpus definition list.</summary>
public sealed record DefinitionItem(IReadOnlyList<Run> Term, IReadOnlyList<Run> Definition);

/// <summary>Corpus <c>term</c> + <c>definition</c>, kept paired so a definition list survives
/// retrieval as a definition list rather than collapsing into prose.</summary>
public sealed record DefinitionParagraph(IReadOnlyList<DefinitionItem> Items) : Paragraph;

public sealed record Section(
    string Tag,
    string Heading,
    IReadOnlyList<Paragraph> Paragraphs,
    string? Href,
    IReadOnlyList<Section> Children,
    string? ImagePrompt = null,
    /// <summary>In-page anchor id for jump links / TOC. Assigned in code after generation —
    /// ignored by the LLM section JSON schema so the model never invents or omits it.</summary>
    [property: JsonIgnore]
    string? Id = null,
    /// <summary>Stage 2 (heading provenance). What licensed this section, in the model's own words
    /// -- "plan", "brief:&lt;fieldName&gt;", "competitor:&lt;heading&gt;", "site:&lt;subtopic&gt;",
    /// "paa:&lt;question&gt;", or "evidence:&lt;partner|section|host&gt;".
    /// ("retrieval:&lt;url&gt;" removed 2026-09-22 -- checked a claimed source URL against an
    /// optional, often-empty research set, so it failed on missing research, not bad output.) The
    /// opposite of <see cref="Id"/>: this flows model -&gt; code,
    /// so it is never <c>[JsonIgnore]</c> -- it must round-trip into the persisted artifact version
    /// row. Left null by callers that don't require provenance (FAQ, tool pages); checked by
    /// <c>GccHeadingProvenanceGuard</c> only where a caller opts in.</summary>
    string? Provenance = null);

/// <summary>
/// A generated body: a lede section (opening hook, always tag "h2") followed by the body's
/// section tree. Per-element addressing (doc.Sections[1].Children[0]) is plain object indexing —
/// no text blob is ever parsed to recover this structure.
/// </summary>
public sealed record ContentDocument(Section Lede, IReadOnlyList<Section> Sections);
