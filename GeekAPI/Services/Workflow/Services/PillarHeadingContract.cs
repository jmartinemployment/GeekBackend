using GeekAPI.Services.Workflow.Domain.Entities;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.Workflow.Services;

/// <summary>
/// The plan owns H2 text.
///
/// Write Body asks the model for one section per planned outline heading and stores the result
/// keyed by that heading, but the section JSON contract never makes the model echo the heading
/// back — it is free to rewrite or truncate it. Two distinct outline entries rewritten to the
/// same string render as duplicate H2s, and because AssignSectionIds runs EnsureUniqueSlug
/// afterwards the ids come out unique ("…-2"), so nothing downstream notices.
///
/// This is the same ownership rule already applied to <see cref="Section.Id"/>: assigned in code
/// after generation so the model can neither invent nor omit it.
/// </summary>
public static class PillarHeadingContract
{
    /// <summary>Normalized form used only to compare two headings for collision. Trims, collapses
    /// whitespace and drops trailing punctuation, so a heading the model truncated at its colon
    /// ("Data Quality Assessments:") is recognised as the same section as "Data Quality
    /// Assessments".</summary>
    public static string HeadingKey(string? heading) =>
        Regex.Replace(heading ?? string.Empty, @"\s+", " ")
            .Trim()
            .TrimEnd(':', '-', '—', '–', '.')
            .Trim()
            .ToLowerInvariant();

    /// <summary>Outline entries that name the same section more than once, each rendered as the
    /// distinct spellings that collided. Empty when the outline is well formed.</summary>
    public static IReadOnlyList<string> FindDuplicateOutlineHeadings(IReadOnlyList<string>? sectionOutline) =>
        (sectionOutline ?? [])
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .GroupBy(HeadingKey, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => string.Join("\" / \"", g.Distinct(StringComparer.Ordinal)))
            .ToList();

    /// <summary>True when the model's heading differs from the planned one in more than
    /// insignificant whitespace — i.e. the text actually drifted and is worth logging.</summary>
    public static bool HeadingDrifted(Section section, string plannedHeading) =>
        !string.IsNullOrWhiteSpace(plannedHeading)
        && !string.Equals(section.Heading?.Trim(), plannedHeading.Trim(), StringComparison.Ordinal);

    /// <summary>Returns the section carrying its planned heading. Only the section's own H2 text is
    /// replaced; paragraphs and children are untouched, so nothing the model wrote is discarded.</summary>
    public static Section WithPlannedHeading(Section section, string plannedHeading) =>
        HeadingDrifted(section, plannedHeading)
            ? section with { Heading = plannedHeading }
            : section;
}
