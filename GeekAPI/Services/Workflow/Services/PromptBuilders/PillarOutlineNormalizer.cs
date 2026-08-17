namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

/// <summary>
/// FAQ heading detection for Write Body routing (skip "People Also Ask" in the main H2 loop).
/// Outline rewrite (Sanitize) is commented out — the stored plan is what gets written.
/// </summary>
public static class PillarOutlineNormalizer
{
    private const string FaqSectionTitle = "People Also Ask";

    /// <summary>
    /// Kept for reference. Do not call — Generate plan and Write Body use the stored outline as planned.
    /// Previously deleted tools-shaped H2s, moved question H2s into FAQ, invented default sections,
    /// and appended People Also Ask.
    /// </summary>
    // public static (List<string> MainOutline, List<string> FaqQuestions) Sanitize(
    //     IReadOnlyList<string> sectionOutline,
    //     IReadOnlyList<string> paaFromResearch,
    //     string? targetKeyword = null)
    // {
    //     var main = new List<string>();
    //     var faqFromOutline = new List<string>();
    //
    //     foreach (var raw in sectionOutline)
    //     {
    //         var item = raw.Trim();
    //         if (item.Length == 0)
    //         {
    //             continue;
    //         }
    //
    //         if (IsFaqSectionTitle(item))
    //         {
    //             continue;
    //         }
    //
    //         if (LooksLikeQuestion(item))
    //         {
    //             faqFromOutline.Add(NormalizeQuestion(item));
    //         }
    //         else if (!IsToolsSection(item))
    //         {
    //             main.Add(item);
    //         }
    //     }
    //
    //     if (main.Count == 0)
    //     {
    //         main.AddRange(DefaultMainSections());
    //     }
    //
    //     if (!main.Any(IsFaqSectionTitle))
    //     {
    //         main.Add(FaqSectionTitle);
    //     }
    //
    //     var allFaq = faqFromOutline
    //         .Concat(paaFromResearch.Select(NormalizeQuestion))
    //         .Where(q => q.Length > 3)
    //         .Distinct(StringComparer.OrdinalIgnoreCase)
    //         .Take(MaxFaqQuestions)
    //         .ToList();
    //
    //     return (main, allFaq);
    // }

    public static bool IsFaqSectionTitle(string heading)
    {
        var text = heading.Trim();
        return text.Equals(FaqSectionTitle, StringComparison.OrdinalIgnoreCase)
               || text.Equals("Frequently Asked Questions", StringComparison.OrdinalIgnoreCase)
               || text.Equals("FAQ", StringComparison.OrdinalIgnoreCase);
    }

    // LooksLikeQuestion / IsToolsSection / NormalizeQuestion / DefaultMainSections
    // were only used by Sanitize. Left unused on purpose.
}
