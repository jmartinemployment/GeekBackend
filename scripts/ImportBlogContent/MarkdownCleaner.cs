using System.Text;
using System.Text.RegularExpressions;

namespace ImportBlogContent;

public static partial class MarkdownCleaner
{
    private static readonly string[] DepartmentDeskLabels =
    [
        "Accounting Desk",
        "Marketing Desk",
        "Sales Desk",
        "Human Resources Desk",
        "Customer Service Desk",
    ];

    public static string Clean(string body, string title)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var lines = body.Replace("\r\n", "\n").Split('\n').ToList();
        RemoveLeadingDepartmentDesk(lines);
        RemoveDuplicateTitleHeading(lines, title);
        RemoveAuthorBylines(lines);
        RemoveStrayBulletLines(lines);
        CollapseLeadingBlankLines(lines);

        return string.Join('\n', lines).Trim();
    }

    private static void RemoveLeadingDepartmentDesk(List<string> lines)
    {
        while (lines.Count > 0)
        {
            var trimmed = lines[0].Trim();
            if (trimmed.Length == 0)
            {
                lines.RemoveAt(0);
                continue;
            }

            if (DepartmentDeskLabels.Any(d => string.Equals(trimmed, d, StringComparison.OrdinalIgnoreCase))
                || DeskLabelRegex().IsMatch(trimmed))
            {
                lines.RemoveAt(0);
                continue;
            }

            break;
        }
    }

    private static void RemoveDuplicateTitleHeading(List<string> lines, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;

        var normalizedTitle = NormalizeComparable(title);

        while (lines.Count > 0)
        {
            var trimmed = lines[0].Trim();
            if (trimmed.Length == 0)
            {
                lines.RemoveAt(0);
                continue;
            }

            var headingText = HeadingRegex().Match(trimmed);
            if (!headingText.Success)
                break;

            if (NormalizeComparable(headingText.Groups[1].Value) == normalizedTitle)
            {
                lines.RemoveAt(0);
                continue;
            }

            break;
        }
    }

    private static void RemoveAuthorBylines(List<string> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (AuthorBylineRegex().IsMatch(trimmed))
                lines.RemoveAt(i);
        }

        // Also remove author lines near the top (before first paragraph block).
        var scanLimit = Math.Min(lines.Count, 8);
        for (var i = scanLimit - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (AuthorBylineRegex().IsMatch(trimmed))
                lines.RemoveAt(i);
        }
    }

    private static void RemoveStrayBulletLines(List<string> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed is "•" or "·" or "*")
                lines.RemoveAt(i);
        }
    }

    private static void CollapseLeadingBlankLines(List<string> lines)
    {
        while (lines.Count > 1 && string.IsNullOrWhiteSpace(lines[0]))
            lines.RemoveAt(0);
    }

    private static string NormalizeComparable(string value)
    {
        var decoded = System.Net.WebUtility.HtmlDecode(value);
        var sb = new StringBuilder(decoded.Length);
        foreach (var ch in decoded.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"^#{1,6}\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^.+\s*Desk$", RegexOptions.IgnoreCase)]
    private static partial Regex DeskLabelRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z .'-]*\s*•\s*$")]
    private static partial Regex AuthorBylineRegex();
}
