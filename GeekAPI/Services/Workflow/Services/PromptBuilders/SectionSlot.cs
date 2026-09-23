namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

/// <summary>
/// One top-level section of a long-form page, expressed as an obligation rather than a title.
///
/// <para>
/// Two shapes, because two paths legitimately differ. <see cref="Assigned"/> carries a heading a
/// planning call already wrote for this specific topic -- the orchestrator plans its outline per
/// keyword, so those headings are the page's own. <see cref="Cover"/> carries what the section must
/// cover and leaves the heading to the writer, because the Create path's outlines are compile-time
/// constants: every pillar shipped "Overview / Why it matters now / How it works / What to evaluate
/// / Implementation path / When it is the right call" and every tool page shipped "Overview / Key
/// Capabilities / How It Works / Implementation Considerations / Evaluation Criteria / When to
/// Use", identically, forever.
/// </para>
///
/// <para>
/// Jeff, 2026-09-23: "Headings are lame and I would bet repeated on every single blog post", then
/// "The headings reflect why content word count is so drastically low." The second half is the
/// mechanism and it is the reason this type exists rather than a prompt line asking for better
/// titles: a category label is a section with nothing in particular to say, so the model says a
/// little of everything and stops. "Key considerations" cannot run long because nothing about it
/// is specific enough to run long. A coverage slot states a claim the section has to land, and a
/// section that has to land a claim has somewhere to go.
/// </para>
///
/// <para>
/// The spine survives the change. A tool page still has to cover what the product does, how it
/// works, what deploying it involves, how to judge it and who it suits -- that obligation is
/// exactly what <see cref="Covers"/> holds. What stops being fixed is the words at the top of the
/// section.
/// </para>
/// </summary>
/// <param name="Heading">The exact heading to write, when one was planned for this page. Null on a
/// coverage slot, where the writer names the section itself.</param>
/// <param name="Covers">What this section is responsible for. Null on an assigned slot.</param>
/// <param name="Depth">Approximate words, for proportion between sections -- never a quota.</param>
/// <param name="Guidance">Section-specific instruction, already resolved against the page's subject
/// (product name, publisher, keyword) by the type that owns the outline.</param>
public sealed record SectionSlot(
    string? Heading = null,
    string? Covers = null,
    string? Depth = null,
    string? Guidance = null)
{
    /// <summary>A heading a planning call wrote for this page. Write it as given.</summary>
    public static SectionSlot Assigned(string heading) => new(Heading: heading);

    /// <summary>An obligation. The writer names the section; this says what it owes the reader.</summary>
    public static SectionSlot Cover(string covers, string? depth = null, string? guidance = null) =>
        new(Covers: covers, Depth: depth, Guidance: guidance);

    /// <summary>True when the writer must name this section itself.</summary>
    public bool WritesItsOwnHeading => Heading is null;

    /// <summary>
    /// What to show in an outline listing. An assigned slot shows its heading; a coverage slot
    /// shows what it covers, which is all there is to show before the writer has named it.
    /// </summary>
    public string Label => Heading ?? Covers
        ?? throw new InvalidOperationException("A section slot carries either a heading or a coverage statement.");
}
