using System.Text;
using System.Text.Json;

namespace GeekAPI.Services.Workflow.Services.JsonLd;

public static class JsonLdSummaryFormatter
{
    public static string Format(JsonLdSiteSummary summary)
    {
        if (!summary.HasContent)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== EXISTING STRUCTURED DATA (JSON+LD) ===");
        AppendSection(sb, "Organization / business", summary.Organizations);
        AppendSection(sb, "People", summary.People);
        AppendSection(sb, "Services & offers", summary.Services);
        AppendSection(sb, "Expertise / knowsAbout", summary.Topics);
        AppendSection(sb, "Service areas", summary.ServiceAreas);
        AppendSection(sb, "FAQ (from site schema)", summary.FaqEntries);
        AppendSection(sb, "Published articles (from site schema)", summary.Articles);
        AppendSection(sb, "Key web pages", summary.WebPages);
        AppendSection(sb, "Software applications (from site schema)", summary.SoftwareApplications);
        return sb.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder sb, string heading, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        sb.AppendLine(heading + ":");
        foreach (var line in lines)
        {
            sb.AppendLine($"- {line}");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Parses <see cref="JsonLdSiteSummary.FaqEntries"/> back into question/answer pairs for
    /// <c>FAQPage</c> emission. Entries are formatted here as <c>"Q: {q} | A: {a}"</c> by
    /// <see cref="JsonLdParserService.FormatQuestion"/> — a question with no answer becomes just
    /// <c>"Q: {q}"</c> and is skipped, since an unanswered question is not a real FAQ pair.
    /// </summary>
    public static IReadOnlyList<GeekAPI.Services.Workflow.DTOs.ContentFaqEntry> ParseFaqEntries(
        IReadOnlyList<string> rawEntries)
    {
        var parsed = new List<GeekAPI.Services.Workflow.DTOs.ContentFaqEntry>();
        foreach (var entry in rawEntries)
        {
            var separator = entry.IndexOf(" | A: ", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var question = entry[2..separator].Trim();
            var answer = entry[(separator + 6)..].Trim();
            if (question.Length > 0 && answer.Length > 0)
            {
                parsed.Add(new GeekAPI.Services.Workflow.DTOs.ContentFaqEntry(question, answer));
            }
        }
        return parsed;
    }
}
