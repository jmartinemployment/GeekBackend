namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Shared body/image-prompt fixtures for the generate-service tests, which is where a long-form
/// page's sections now come back one batch at a time rather than in a single response.
/// </summary>
/// <remarks>
/// <para>
/// Batching broke every fixture that answered by call index or returned the whole body on every
/// body call: the first built a page out of three copies of one section, the second answered a
/// later call's question with an earlier call's content. Both failures are about the fixture, not
/// about anything the tests assert, so the shape lives here once instead of in each file.
/// </para>
/// <para>
/// Nothing here encodes how many batches a page takes. That is the outline divided by
/// <c>SectionsPerBatch</c>, an implementation detail no test in this folder is about, and a fixture
/// that hard-codes it goes red the next time either changes.
/// </para>
/// </remarks>
internal static class ScriptedBody
{
    /// <summary>
    /// A sections array for a body batch the test did not script itself: one plan-tagged h2, named
    /// for the call that produced it so the assembled page has distinct headings.
    /// </summary>
    /// <remarks>
    /// "plan" is the provenance the outline's own sections carry, so the heading-provenance guard
    /// accepts it exactly as it accepts a real assigned section — filler here is licensed the same
    /// way the thing it stands in for would be.
    /// </remarks>
    internal static string PlannedBatch(int callIndex) =>
        $$"""
        {"sections":[{"tag":"h2","heading":"Planned section {{callIndex + 1}}","paragraphs":[{"type":"text","runs":[{"text":"Body."}]}],"href":null,"children":[],"provenance":"plan"}]}
        """;

    /// <summary>
    /// Image prompts with headroom. The service refuses a list shorter than one hero plus one per
    /// H2 — a short list used to be absorbed, leaving sections silently unprompted — and accepts a
    /// longer one, so a generous list is the fixture that does not depend on the batch count.
    /// </summary>
    internal static string ImagePrompts(int count = 16) =>
        $$"""{"prompts":[{{string.Join(",", Enumerable.Range(0, count).Select(i =>
            $$"""{"section":"Section {{i}}","prompt":"image prompt {{i}}"}"""))}}]}""";
}
