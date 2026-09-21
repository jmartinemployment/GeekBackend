using System.Text.Json.Serialization;

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

public sealed record Run(string Text, bool Bold = false, bool Italic = false, string? Href = null);

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
/// </remarks>
public abstract record Paragraph;

public sealed record TextParagraph(IReadOnlyList<Run> Runs) : Paragraph;

public sealed record ListParagraph(bool Ordered, IReadOnlyList<IReadOnlyList<Run>> Items) : Paragraph;

/// <summary>Corpus <c>quote</c>. <paramref name="Cite"/> is the source URL when the quote came from
/// a retrieved passage — the attribution half of "cite or quote Partners/Tools".</summary>
public sealed record QuoteParagraph(IReadOnlyList<Run> Runs, string? Cite = null) : Paragraph;

/// <summary>Corpus <c>code</c>. Held as raw text, never runs: bold/italic/href have no meaning
/// inside a code block, and cliché cleaning must not rewrite it.</summary>
public sealed record CodeParagraph(string Code, string? Language = null) : Paragraph;

/// <summary>One <c>term</c>/<c>definition</c> pair from a corpus definition list.</summary>
public sealed record DefinitionItem(IReadOnlyList<Run> Term, IReadOnlyList<Run> Definition);

/// <summary>Corpus <c>term</c> + <c>definition</c>, kept paired so a glossary survives retrieval
/// as a glossary rather than collapsing into prose.</summary>
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
    string? Id = null);

/// <summary>
/// A generated body: a lede section (opening hook, always tag "h2") followed by the body's
/// section tree. Per-element addressing (doc.Sections[1].Children[0]) is plain object indexing —
/// no text blob is ever parsed to recover this structure.
/// </summary>
public sealed record ContentDocument(Section Lede, IReadOnlyList<Section> Sections);
