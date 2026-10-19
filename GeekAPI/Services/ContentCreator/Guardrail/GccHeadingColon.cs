using GeekAPI.Services.Gcw;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreator.Guardrail;

/// <summary>
/// A heading split in two by a colon keeps the part before it (Jeff, 2026-10-18: "no more colons").
/// "How Marketmuse Is Built: Architecture, Data, and What It Connects" becomes "How Marketmuse Is Built".
/// </summary>
/// <remarks>
/// <para>
/// <b>Why code and not only the instruction.</b> Every writer is told a heading is one phrase
/// (<c>ContentPromptBuilder.HeadingColonInstruction</c>). The first run told it, at 15:06 UTC on
/// 2026-10-18, still wrote three of MarketMuse's tool page headings as "subject: details": the
/// instruction sits in the system prompt, and each section is assigned in a request of about 320,000
/// characters as "Cover: how Marketmuse is built, its real architecture, what it connects to and what it
/// holds", a subject and its details, which GPT-4.1 sets as a title and a subtitle. As with the em dash
/// (<see cref="GccEmDash"/>), an instruction is not a guarantee.
/// </para>
/// <para>
/// <b>Code deletes; it does not rewrite.</b> The part before the colon is the claim, and in every heading
/// of that kind on the pages of 2026-10-13 to 2026-10-18 it stands as a heading on its own; the part after
/// is the list of what the section covers, which the section itself then says.
/// </para>
/// <para>
/// <b>Left as written,</b> and still listed by <see cref="GccDraftGuard"/>'s <c>heading-colon</c> line:
/// a heading that ends "?" (one of the operator's FAQ questions, his words); a heading whose part before
/// the colon is a single word ("Overview: ..."), which is no heading on its own; and a heading whose part
/// after the colon carries the page's keyword when no other heading on the page does, since cutting it
/// would take the page's one keyword heading away.
/// </para>
/// </remarks>
public static class GccHeadingColon
{
    /// <summary>One heading cut: as the writer wrote it, and as it is saved.</summary>
    public sealed record Cut(string Written, string Saved);

    /// <summary><paramref name="document"/> with each heading's part after a colon taken off, and the cuts made.</summary>
    public static (ContentDocument Document, IReadOnlyList<Cut> Cuts) Remove(ContentDocument document, string? keyword)
    {
        var phrase = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var headings = new List<string>();
        Collect(document.Lede, headings);
        foreach (var section in document.Sections) Collect(section, headings);

        var cuts = new List<Cut>();
        var lede = CutSection(document.Lede, phrase, headings, cuts);
        var sections = document.Sections.Select(section => CutSection(section, phrase, headings, cuts)).ToList();
        return (cuts.Count == 0 ? document : new ContentDocument(lede, sections), cuts);
    }

    /// <summary>The heading as it is saved, or null when it is left as written.</summary>
    internal static string? CutOf(string heading, string? phrase, IReadOnlyList<string> pageHeadings)
    {
        var trimmed = heading.Trim();
        if (trimmed.EndsWith('?')) return null;

        var colon = trimmed.IndexOf(": ", StringComparison.Ordinal);
        if (colon < 0)
        {
            if (!trimmed.EndsWith(':')) return null;
            colon = trimmed.Length - 1;
        }

        var before = trimmed[..colon].Trim();
        var after = trimmed[(colon + 1)..].Trim();
        if (before.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2) return null;

        if (phrase is not null
            && Carries(after, phrase)
            && !Carries(before, phrase)
            && pageHeadings.Count(other => Carries(other, phrase)) <= 1)
        {
            return null;
        }

        return before;
    }

    private static bool Carries(string text, string phrase) => GcwSeoAnalyzer.CountPhraseOccurrences(text, phrase) > 0;

    private static void Collect(Section section, List<string> into)
    {
        if (!string.IsNullOrWhiteSpace(section.Heading)) into.Add(section.Heading);
        foreach (var child in section.Children) Collect(child, into);
    }

    private static Section CutSection(Section section, string? phrase, IReadOnlyList<string> pageHeadings, List<Cut> cuts)
    {
        var heading = section.Heading;
        if (!string.IsNullOrWhiteSpace(heading) && CutOf(heading, phrase, pageHeadings) is { } saved)
        {
            cuts.Add(new Cut(heading.Trim(), saved));
            heading = saved;
        }

        var children = section.Children.Select(child => CutSection(child, phrase, pageHeadings, cuts)).ToList();
        return section with { Heading = heading, Children = children };
    }
}
