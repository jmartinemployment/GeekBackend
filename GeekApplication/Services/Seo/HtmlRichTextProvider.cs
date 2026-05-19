using System.Text.RegularExpressions;
using GeekApplication.Interfaces.Seo;

namespace GeekApplication.Services.Seo;

public sealed partial class HtmlRichTextProvider : IRichTextProvider
{
    public string ProviderName => "html";

    public string ExtractPlainText(string html) =>
        HtmlTagRegex().Replace(html, " ").Trim();

    public int CountWords(string html)
    {
        var text = ExtractPlainText(html);
        return string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.None)]
    private static partial Regex HtmlTagRegex();
}
