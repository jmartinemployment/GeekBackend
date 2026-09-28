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
public static class GccToolsSectionGuard
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
                && PillarSectionClassifier.IsToolsListingHeading(section.Heading))
            {
                found.Add(section.Heading);
            }

            if (section.Children.Count > 0) Walk(section.Children, found);
        }
    }

    /// <summary>What to tell the writer when it wrote one anyway.</summary>
    public static string RetryInstruction(IReadOnlyList<string> headings) =>
        $"REMOVE THE TOOLS SECTION -- {string.Join(", ", headings.Select(h => $"\"{h}\""))}. "
        + "This page does not carry a section whose job is to list tools, whatever it is called, and "
        + "a heading of that shape existing on the site does not license one here. Keep the products "
        + "themselves: name each where it earns the mention in the prose of the section it belongs "
        + "to, and link the first substantive mention. Do not simply delete the material -- the page "
        + "is not shorter for losing the heading.";
}
