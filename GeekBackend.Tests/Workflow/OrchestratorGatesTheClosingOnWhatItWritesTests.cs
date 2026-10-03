using Xunit;

namespace GeekBackend.Tests.Workflow;

/// <summary>
/// The orchestrator gates the page's closing on the sections it actually writes.
/// </summary>
/// <remarks>
/// <para>
/// <c>OwnsTheClosing</c> compares a call's last heading with the last entry of the
/// <c>fullOutline</c> it was handed. The orchestrator iterates <c>mainSections</c> — the outline with
/// the FAQ stripped — while <c>metadata.SectionOutline</c> ends with "People Also Ask", because the
/// plan prompt requires it to. Handing the unstripped outline to the section builders made the two
/// lists disagree by exactly one entry, so no call's last heading ever matched and the page's closing
/// was never written by anyone: <c>BuildArticleFaqSectionPrompt</c> carries no closing either.
/// </para>
/// <para>
/// Asserted on the source because this is a <b>wiring</b> fact. The prompt-builder tests construct
/// their own <c>fullOutline</c> and pass whatever shape they like — they proved the gate works and
/// could not see that production never produces that shape. <c>no-unwired-code.mdc</c>: "A test that
/// constructs its own input proves the callee, never the wiring."
/// </para>
/// </remarks>
public class OrchestratorGatesTheClosingOnWhatItWritesTests
{
    [Fact]
    public void Section_builders_receive_the_FAQ_stripped_outline()
    {
        var src = Source("GeekAPI/Services/Workflow/Services/ContentGenerationOrchestrator.cs");

        // The per-section call: totalSections is already mainSections.Count, so fullOutline must match it.
        Assert.Contains(
            "context, metadata, heading, i, mainSections.Count, mainSections, isRegeneration, revisionNotes)",
            src,
            StringComparison.Ordinal);

        // The batched call: its slots come from mainSections, so the outline it is judged against must too.
        Assert.Contains("[.. mainSections.Select(SectionSlot.Assigned)]", src, StringComparison.Ordinal);

        // The shape that silently removed every closing, on the per-section call.
        Assert.DoesNotContain(
            "mainSections.Count, metadata.SectionOutline", src, StringComparison.Ordinal);

        // metadata.SectionOutline is still correct for BuildPillarLedePrompt, which takes fullOutline
        // as CONTEXT and does not gate on it -- only BuildArticleSectionPrompt (:1931) and
        // BuildArticleSectionBatchPrompt (:1783) call BatchClosingInstruction. Asserting its total
        // absence would forbid a correct use, which is why this names the two gating call shapes
        // instead of banning the identifier.
        Assert.Contains(
            "BuildPillarLedePrompt", src, StringComparison.Ordinal);
    }

    private static string Source(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, relative)))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, relative));
    }
}
