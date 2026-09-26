using System.Text.RegularExpressions;

namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

internal static class PillarSectionClassifier
{
    /// <summary>
    /// Strict test for a heading that names a standalone tools listing — the H2 the pillar must
    /// not carry, because Generate Tools owns that content.
    /// <para>
    /// Only the word "tool" or "tools" counts. A looser predicate used to sit beside this one,
    /// matching "platform", "solution", "vendor" and friends so a Tools section could be found and
    /// written; it went with the section. The looseness is why the two were never merged: this
    /// predicate rejects a plan outright, so a false positive blocks valid work — it flagged
    /// "Common Challenges and Solutions" on the word "solution".
    /// </para>
    /// </summary>
    public static bool IsToolsListingHeading(string sectionHeading) =>
        Regex.IsMatch(sectionHeading ?? string.Empty, @"\btools?\b", RegexOptions.IgnoreCase);

    public static bool IsBestPracticesSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        ReadOnlySpan<string> markers = ["best practice", "checklist", "how to succeed", "successful"];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsBenefitsSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        ReadOnlySpan<string> markers = ["benefit", "advantage", "value of", "roi", "return on"];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsIntroductionSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        ReadOnlySpan<string> markers =
        [
            "introduction", "overview", "what is", "understanding", "getting started with"
        ];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsImplementationSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        // Avoid matching "how to succeed" best-practices headings — those use IsBestPracticesSection.
        if (text.Contains("how to succeed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<string> markers =
        [
            "implement", "implementation", "deploy", "deployment", "adoption", "getting started", "how to"
        ];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsFutureTrendsSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        ReadOnlySpan<string> markers = ["future", "trend", "what's next", "emerging", "outlook"];

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The pillar's single FAQ section, where People Also Ask questions are answered.
    /// Excluded from the main body sections because it is written by its own prompt.</summary>
    public static bool IsFaqSectionTitle(string heading)
    {
        var text = heading.Trim();
        return text.Equals(FaqSectionTitle, StringComparison.OrdinalIgnoreCase)
               || text.Equals("Frequently Asked Questions", StringComparison.OrdinalIgnoreCase)
               || text.Equals("FAQ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Canonical title for that section; the plan prompt asks for exactly this text.</summary>
    public const string FaqSectionTitle = "People Also Ask";
}
