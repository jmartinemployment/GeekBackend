namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

internal static class PillarSectionClassifier
{
    public static bool IsToolsSection(string sectionHeading)
    {
        var text = sectionHeading.Trim();
        ReadOnlySpan<string> markers =
        [
            "tool", "platform", "software", "vendor", "solution", "stack", "technology"
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
