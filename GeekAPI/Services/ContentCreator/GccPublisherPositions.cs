using System.Text;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// How the publisher's own positions are put in front of the writer, and what the writer is told to
/// do with them.
/// </summary>
/// <remarks>
/// <para>
/// The positions are the publisher's, stated on their own site: argued as the publisher's own, never
/// attributed to a third party, never contradicted, never reprinted. The writer is not told which
/// heading is which -- the site decides what it states -- but is told how each kind is used: a method
/// is what the "how it works" section walks, stage by stage; a statement of what the publisher covers
/// or connects is what the page covers; and the closing is the reader's entry into the method (Jeff,
/// 2026-10-06: "The Scheduler and closing questions relate" -- book through the scheduler, answer the
/// publisher's questions when booking, and that is the method's first step).
/// </para>
/// </remarks>
public static class GccPublisherPositions
{
    public const string Header = "=== THE PUBLISHER'S OWN POSITIONS";

    /// <summary>
    /// The guidance on the section that describes how the approach works, for the pillar and the blog.
    /// Stated on the slot as well as in the block, so the section that owes it is told at the point it
    /// is written.
    /// </summary>
    public const string ApproachSlotGuidance =
        "Where THE PUBLISHER'S OWN POSITIONS states how the publisher works -- a methodology, a process, "
        + "stages -- this section walks that method's stages, in order, in your own prose, applied to "
        + "this page's subject. That is the method this page describes, not a generic one and not a vendor's.";

    /// <summary>
    /// The block, or an empty string when the research carries no positions. A tool the project does
    /// not list -- a partner of another project, named on the site as one the publisher implements --
    /// reads as "another tool", since this page may not name it (the 16:52 run of 2026-10-06: the blog
    /// named Tipalti from the site's own Seamless Integrations text).
    /// </summary>
    public static string Block(IReadOnlyList<GccPublisherPosition>? positions, string keyword, IReadOnlyList<string>? unlistedTools = null)
    {
        if (positions is not { Count: > 0 }) return string.Empty;

        var names = GccCompetitorNames.Names(unlistedTools ?? []);
        string Clean(string text) => GccCompetitorNames.Redact(text, names, GccCompetitorNames.AnotherTool);

        var sb = new StringBuilder();
        var url = positions[0].Url;
        sb.AppendLine($"{Header} (from {url}) ===");
        sb.AppendLine("What this publisher states on their own site, by heading. These are the publisher's own");
        sb.AppendLine("positions, not retrieved third-party evidence: argue them as the publisher's own, in your own");
        sb.AppendLine($"prose, applied to {keyword}. Never attribute one to a source, never contradict one, and never");
        sb.AppendLine("reprint a passage. The rules by kind:");
        sb.AppendLine("1. Where a position is how the publisher works -- a methodology, a process, stages -- the section");
        sb.AppendLine("   on how the approach works walks its stages, in order. That is the method this page describes,");
        sb.AppendLine("   not a generic one and not a vendor's.");
        sb.AppendLine("2. Where a position names what the publisher covers or connects -- use cases, integrations, an");
        sb.AppendLine("   outcome they promise -- the page covers it, and the partner tools appear where they do that");
        sb.AppendLine("   work, with the evidence for it.");
        sb.AppendLine("3. The closing is the reader's entry into the publisher's method: book the appointment through");
        sb.AppendLine("   the scheduler, and answer the publisher's questions when booking. That is the method's first");
        sb.AppendLine("   step, not a call to action added at the end.");
        sb.AppendLine();

        foreach (var position in positions)
        {
            sb.AppendLine($"[{Clean(position.Heading)}]");
            foreach (var line in position.Paragraphs)
                sb.AppendLine($"- {Clean(line)}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
