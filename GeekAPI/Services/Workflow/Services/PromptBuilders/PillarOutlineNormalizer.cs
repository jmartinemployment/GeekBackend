namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

/// <summary>
/// Keeps pillar outlines technical: declarative main H2s + a single FAQ section.
/// Question-shaped outline items are moved to the FAQ list (common when SERP/PAA dominate research).
/// Tools sections are stripped — tools are owned by the Tools generation step.
/// </summary>
public static class PillarOutlineNormalizer
{
    private const string FaqSectionTitle = "People Also Ask";
    public const int MaxFaqQuestions = 12;

    public static (List<string> MainOutline, List<string> FaqQuestions) Sanitize(
        IReadOnlyList<string> sectionOutline,
        IReadOnlyList<string> paaFromResearch,
        string? targetKeyword = null)
    {
        var main = new List<string>();
        var faqFromOutline = new List<string>();

        foreach (var raw in sectionOutline)
        {
            var item = raw.Trim();
            if (item.Length == 0)
            {
                continue;
            }

            if (IsFaqSectionTitle(item))
            {
                continue;
            }

            if (LooksLikeQuestion(item))
            {
                faqFromOutline.Add(NormalizeQuestion(item));
            }
            else if (!IsToolsSection(item))
            {
                main.Add(item);
            }
        }

        if (main.Count == 0)
        {
            main.AddRange(DefaultMainSections());
        }

        if (!main.Any(IsFaqSectionTitle))
        {
            main.Add(FaqSectionTitle);
        }

        var allFaq = faqFromOutline
            .Concat(paaFromResearch.Select(NormalizeQuestion))
            .Where(q => q.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxFaqQuestions)
            .ToList();

        return (main, allFaq);
    }

    public static bool LooksLikeQuestion(string heading)
    {
        var text = heading.Trim();
        if (text.EndsWith('?'))
        {
            return true;
        }

        ReadOnlySpan<string> prefixes =
        [
            "what ", "how ", "why ", "when ", "where ", "who ",
            "is ", "are ", "can ", "does ", "do ", "should ", "will "
        ];

        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsFaqSectionTitle(string heading)
    {
        var text = heading.Trim();
        return text.Equals(FaqSectionTitle, StringComparison.OrdinalIgnoreCase)
               || text.Equals("Frequently Asked Questions", StringComparison.OrdinalIgnoreCase)
               || text.Equals("FAQ", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsToolsSection(string heading) => PillarSectionClassifier.IsToolsSection(heading);

    private static string NormalizeQuestion(string question)
    {
        var text = question.Trim();
        return text.EndsWith('?') ? text : $"{text}?";
    }

    private static IEnumerable<string> DefaultMainSections() =>
    [
        "Overview and Key Concepts",
        "Technical Architecture and Implementation",
        "Best Practices and Common Pitfalls",
        "Business Impact and ROI"
    ];
}
