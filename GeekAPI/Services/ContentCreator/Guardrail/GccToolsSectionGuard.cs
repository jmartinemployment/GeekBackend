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
    /// using tools, and both were rejected, because any honest heading for that material contains the
    /// word.
    /// </para>
    /// <para>
    /// A listing announces itself two ways, and both are structural rather than lexical. The
    /// heading enumerates — "Top 5 … Tools", "Best … Tools", "7 Tools to Consider". Or the section
    /// is built as a list: three or more children, each a short product name rather than a
    /// statement or a question. A section that does neither is prose that mentions tools, which is
    /// exactly what the page is meant to carry.
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
}
