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
/// <param name="Depth">A words range for this section, e.g. "600-850 words". The upper figure sizes
/// it against its neighbours, but the lower figure is not advisory: GccGenerateService.BatchFloorWords
/// sums every assigned slot's lower figure into the batch's word-count floor, and
/// ContentPromptBuilder states that same sum to the writer as what is owed. A draft under it is
/// reported as a shortfall. (This doc comment called the field "never a quota" until 2026-10-09; the
/// two enforcement points above disagreed with it the whole time.)</param>
/// <param name="Guidance">Section-specific instruction, already resolved against the page's subject
/// (product name, publisher, keyword) by the type that owns the outline.</param>
public sealed record SectionSlot(
    string? Heading = null,
    string? Covers = null,
    string? Depth = null,
    string? Guidance = null)
{
    /// <summary>
    /// True when the page's keyword-bearing H2 belongs in this section. The type that owns the outline says which
    /// one, because only it knows which section can carry the phrase honestly: the Blog's first two sections are
    /// about doing the work <i>by hand</i>, and a heading there cannot read "Automated Approval Workflows"
    /// without contradicting what the section covers (the Blog of 2026-10-07 wrote "Manual Approval Workflows"
    /// and was reported as having no keyword heading). Read through <see cref="BatchOwnsKeywordHeading"/>.
    /// </summary>
    /// <remarks>
    /// The Pillar and the Tool page name theirs since 2026-10-10. Until then both left it to the first call,
    /// and when each became ten sections the first call's first section was the work done by hand: every tool
    /// page of that day's run has a heading reading "Manual Automated Accounts Receivable", and the pillar,
    /// whose first call is two by-hand sections, rightly wrote no keyword heading there and was reported for it
    /// while eight of its later headings carried the phrase.
    /// </remarks>
    public bool OwnsKeywordHeading { get; init; }

    /// <summary>
    /// The section in <paramref name="batch"/> that carries the page's keyword heading, or null when the
    /// outline names none or another call holds it. What the body prompt names to the writer, so the phrase
    /// goes on that section's heading and not on whichever comes first.
    /// </summary>
    public static SectionSlot? KeywordHeadingOwnerIn(IReadOnlyList<SectionSlot> batch) =>
        batch.FirstOrDefault(slot => slot.OwnsKeywordHeading);

    /// <summary>
    /// Whether a call writing <paramref name="batch"/> is the one asked for the page's keyword heading, and the one
    /// held to it. One definition for the prompt that asks and the check that counts, so a batch is never asked
    /// for something it is not measured on or measured on something it was not asked for.
    /// </summary>
    /// <remarks>
    /// An outline that names an owner asks the call that holds it. One that names none keeps the rule every
    /// type had until 2026-10-07: the first batch is asked. Every Content Creator type names one; an outline a
    /// planning call wrote names none. Whether the page has such a heading is judged once, on the finished
    /// page, by <c>GccDraftGuard</c>; no call is reported for it.
    /// </remarks>
    public static bool BatchOwnsKeywordHeading(
        IReadOnlyList<SectionSlot> batch, IReadOnlyList<SectionSlot>? fullOutline, int batchIndex) =>
        fullOutline is { } outline && outline.Any(slot => slot.OwnsKeywordHeading)
            ? batch.Any(slot => slot.OwnsKeywordHeading)
            : batchIndex == 0;

    /// <summary>
    /// The words this section owes its page: what the call that writes it is checked against. Separate from
    /// <see cref="Depth"/>, which is the size the writer is asked for.
    /// </summary>
    /// <remarks>
    /// The two were one number until 2026-10-10 -- "the lower figure is owed" -- and it was a wish. A body call
    /// stops on its own at about 650 words (21 calls of 2026-10-07, none cut off), so two sections asked for
    /// 500-700 each came back at 640 against a 1,000-word floor on every page. Asking for less is not the fix:
    /// the same log shows the writer giving about a third of a word less for each word less it is asked. So the
    /// ask stays where it is and the floor becomes the section's true share of the page's floor
    /// (<see cref="WithOwedWords"/>). Null means the slot has no such share and is held to its depth's lower
    /// figure, as every slot was.
    /// </remarks>
    public int? OwedWords { get; init; }

    /// <summary>
    /// <paramref name="slots"/> with <paramref name="pageFloor"/> divided across them, so the sections together
    /// owe exactly the page's floor. A remainder is spread a word at a time along the outline rather than left on
    /// one section: any two calls that write the same number of sections differ by at most one word.
    /// </summary>
    public static IReadOnlyList<SectionSlot> WithOwedWords(IReadOnlyList<SectionSlot> slots, int pageFloor)
    {
        if (slots.Count == 0 || pageFloor <= 0) return slots;

        var owed = new List<SectionSlot>(slots.Count);
        for (var i = 0; i < slots.Count; i++)
        {
            var throughThisOne = (int)((long)(i + 1) * pageFloor / slots.Count);
            var beforeThisOne = (int)((long)i * pageFloor / slots.Count);
            owed.Add(slots[i] with { OwedWords = throughThisOne - beforeThisOne });
        }

        return owed;
    }

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
