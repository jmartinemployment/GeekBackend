using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// No section on a pillar or a blog whose job is to list tools.
///
/// <para>
/// Jeff has said this three times. The ban was written into the outline prompts, where it stops a
/// plan carrying one -- and a body prompt may add sections beyond its outline, so the plan came
/// back clean and the body wrote "Top Tools for Automated Data Entry &amp; Processing" with one h3
/// per product underneath it. Banned at planning time and not at writing time is banned nowhere.
/// </para>
///
/// <para>
/// It surfaced as a provenance error, because the model tried to license the section against a real
/// "Top 5 ... Tools" heading on the site and that heading is not one of the must-mention subtopics.
/// The refusal was right and unreadable: the problem is not the tag, it is the section.
/// <c>PillarHeadingContract.FindToolsOutlineHeadings</c> has always been able to spot these and is
/// documented "Reported, never rejected" -- reporting to a log nobody reads is how this shipped
/// repeatedly.
/// </para>
/// </summary>
public static partial class GccToolsSectionGuard
{
    /// <summary>
    /// Every section in the tree whose heading is a tools listing, at any depth. Empty means the
    /// draft carries none.
    /// </summary>
    public static IReadOnlyList<string> FindToolsSections(IReadOnlyList<Section> sections)
    {
        var found = new List<string>();
        Walk(sections, found);
        return found;
    }

    /// <summary>
    /// The planned headings that are tools listings — checked before a word is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is where the rule had to move to stop recurring.</b> The guard only ever ran on the
    /// written body, and the body writer receives a planned outline as <i>assigned slots</i>. So the
    /// metadata call proposed "Choosing the Right AI Tools for Accounts Payable Automation", the writer
    /// wrote the heading it was handed — the reasonable reading of an assignment — and the retry
    /// re-wrote against the same outline, so no retry could ever succeed. Jeff, three times, most
    /// recently 2026-10-02: a pillar and a blog do not contain a tools section, period.
    /// </para>
    /// <para>
    /// No wording in the body prompt can fix that, because the writer is not disobeying: it is obeying
    /// the outline. Catching it at plan time means the writer is never handed the heading, and the whole
    /// check costs one metadata call rather than five body calls and a refusal.
    /// </para>
    /// <para>
    /// Heading text only, so it works on an outline. <see cref="FindToolsSections"/> additionally
    /// accepts a section whose <i>children</i> read as a product list — that shape cannot exist yet at
    /// plan time, so it is not tested here and is still caught in the body.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> FindToolsHeadings(IEnumerable<string>? headings) =>
        [.. (headings ?? []).Where(IsToolsListingHeadingText)];

    /// <summary>Whether a heading on its own announces a tools listing.</summary>
    public static bool IsToolsListingHeadingText(string? heading) =>
        !string.IsNullOrWhiteSpace(heading)
        && PillarSectionClassifier.IsToolsListingHeading(heading)
        && Enumerates(heading);

    /// <summary>What to tell the planner so the next outline does not carry one.</summary>
    public static string OutlineRetryInstruction(IReadOnlyList<string> headings) =>
        "THE OUTLINE YOU RETURNED IS REJECTED. These planned headings are tools listings, which this "
        + "page must not contain: "
        + string.Join(", ", headings.Select(h => $"\"{h}\""))
        + ". Replace each one with a heading about the problem that section solves. The tools belong in "
        + "the prose of those sections -- named where each earns the mention, saying what it does about "
        + "that problem -- never in a heading and never as a section of their own. Return the whole "
        + "metadata object again.";

    private static void Walk(IReadOnlyList<Section> sections, List<string> found)
    {
        foreach (var section in sections)
        {
            // Only the top-level shape is a "tools section". An h3 named for one product is how a
            // tools section is built, but on its own -- under a section about something else -- a
            // product-named subheading is ordinary writing.
            if (!string.IsNullOrWhiteSpace(section.Heading)
                && string.Equals(section.Tag, "h2", StringComparison.OrdinalIgnoreCase)
                && IsListing(section))
            {
                found.Add(section.Heading);
            }

            if (section.Children.Count > 0) Walk(section.Children, found);
        }
    }

    /// <summary>
    /// Whether this section's <i>job</i> is to list tools — which the heading alone cannot say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be <c>PillarSectionClassifier.IsToolsListingHeading</c>, which is
    /// <c>t o o l s ?</c> — any h2 containing the word. That is not what this guard is for, and
    /// it refused work it was never meant to touch: "How AI Tools Simplify Your Accounts Payable
    /// Process" and "Choosing the Right AI Tool for Your Business Needs" are prose sections about
    /// using tools, and both were rejected. The retry could not save them either, because any
    /// honest heading for that material contains the word.
    /// </para>
    /// <para>
    /// A listing announces itself two ways, and both are structural rather than lexical. The
    /// heading enumerates — "Top 5 … Tools", "Best … Tools", "7 Tools to Consider". Or the section
    /// is built as a list: three or more children, each a short product name rather than a
    /// statement or a question. A section that does neither is prose that mentions tools, which is
    /// exactly what the retry instruction asks the writer to produce.
    /// </para>
    /// <para>
    /// The word is still required. Widening to "platform" and "solution" is what
    /// <c>IsToolsListingHeading</c>'s own doc warns against — it flagged "Common Challenges and
    /// Solutions" — and a guard that rejects a draft outright cannot afford that.
    /// </para>
    /// </remarks>
    private static bool IsListing(Section section) =>
        PillarSectionClassifier.IsToolsListingHeading(section.Heading)
        && (Enumerates(section.Heading) || ReadsAsAList(section.Children));

    /// <summary>
    /// A heading the writer was explicitly told not to write.
    /// </summary>
    /// <remarks>
    /// These are not this guard's invention. <c>ContentPromptBuilder.NoToolsSectionInstruction</c>
    /// names them to the writer: <i>"not \"Top Tools for ...\", not \"Choosing the Right Tools\",
    /// not a heading per product with a product name in it"</i>. The guard enforces that list and
    /// nothing beyond it — a guard stricter than its prompt refuses work the writer was never told
    /// to avoid, and a guard looser than its prompt lets through what the prompt forbids. Both are
    /// drift; the second is what this method was missing.
    /// </remarks>
    private static bool Enumerates(string heading) =>
        EnumerativeHeading().IsMatch(heading) || SelectionHeading().IsMatch(heading);

    /// <summary>
    /// Children that are product names rather than prose. Three, because two sub-sections under a
    /// section about tools is ordinary structure; a list starts at three.
    /// </summary>
    private static bool ReadsAsAList(IReadOnlyList<Section> children)
    {
        if (children.Count < 3) return false;

        var namelike = children.Count(c => IsProductName(c.Heading));
        return namelike >= 3 && namelike * 2 >= children.Count;
    }

    /// <summary>
    /// Short, not a question, and not a statement — "Tipalti", "Bill.com AP", "Stampli". A heading
    /// that opens with how/why/what/when/should is the writer explaining something, whatever it is
    /// named after.
    /// </summary>
    private static bool IsProductName(string? heading)
    {
        var text = (heading ?? string.Empty).Trim();
        if (text.Length == 0 || text.EndsWith('?')) return false;
        if (ProsePrefix().IsMatch(text)) return false;

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5;
    }

    [GeneratedRegex(@"\b(top|best|leading|favou?rite)\b.*\btools?\b"
        + @"|\b\d+\s+(\w+\s+){0,3}tools?\b"
        + @"|\btools?\b[^.]*\b(compared|comparison|round-?up|shortlist|options|we recommend|to consider|ranked)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex EnumerativeHeading();

    [GeneratedRegex(@"^(how|why|what|when|where|should|can|do|does|is|are|choosing|picking|selecting|using)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ProsePrefix();

    /// <summary>"Choosing the Right Tools", and the ways of saying it the prompt means.</summary>
    [GeneratedRegex(@"\b(choos|pick|select|find)\w*\s+(the\s+)?(right|best|ideal)\b[^.]*\btools?\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex SelectionHeading();

    /// <summary>What to tell the writer when it wrote one anyway.</summary>
    public static string RetryInstruction(IReadOnlyList<string> headings) =>
        $"REMOVE THE TOOLS SECTION -- {string.Join(", ", headings.Select(h => $"\"{h}\""))}. "
        + "This page does not carry a section whose job is to list tools, whatever it is called, and "
        + "a heading of that shape existing on the site does not license one here. Keep the products "
        + "themselves: name each where it earns the mention in the prose of the section it belongs "
        + "to, and link the first substantive mention. Do not simply delete the material -- the page "
        + "is not shorter for losing the heading.";
}
