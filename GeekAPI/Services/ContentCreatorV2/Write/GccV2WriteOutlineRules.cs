using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Services.ContentCreatorV2.Write;

/// <summary>Outline/lede overlap rules ported from workflow v1 — the pillar lede call covers outline H2 #1.</summary>
internal static class GccV2WriteOutlineRules
{
    internal static bool HeadingsEqual(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(NormalizeHeading(a), NormalizeHeading(b), StringComparison.Ordinal);
    }

    internal static string NormalizeHeading(string value) =>
        value.Trim().TrimEnd(':').Trim();

    /// <summary>When the combined lede+introduction response uses the same H2 twice, merge like workflow v1.</summary>
    internal static Section MergeLedeAndIntroduction(Section lede, Section introduction)
    {
        if (!HeadingsEqual(lede.Heading, introduction.Heading))
            return lede;

        return lede with
        {
            Paragraphs = lede.Paragraphs.Concat(introduction.Paragraphs).ToList(),
            Children = introduction.Children.Count > 0 ? introduction.Children : lede.Children,
            ImagePrompt = lede.ImagePrompt ?? introduction.ImagePrompt,
        };
    }

    /// <summary>
    /// Pillar lede prompt writes outline H2 #1 — skip re-drafting it. Blog only skips on an exact heading match.
    /// </summary>
    internal static int FirstBodyOutlineIndex(string ledeHeading, IReadOnlyList<GccV2OutlineSection> outline, bool pillar)
    {
        if (outline.Count == 0) return 0;
        if (pillar) return 1;
        return HeadingsEqual(ledeHeading, outline[0].Heading) ? 1 : 0;
    }
}
