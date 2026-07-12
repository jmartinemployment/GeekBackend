using ReverseMarkdown;

namespace ImportBlogContent;

public static class HtmlToMarkdown
{
    private static readonly Converter Converter = CreateConverter();

    public static string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var trimmed = html.Trim();
        if (!LooksLikeHtml(trimmed))
            return trimmed;

        return Converter.Convert(trimmed).Trim();
    }

    public static bool LooksLikeHtmlForMigration(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return trimmed.StartsWith('<') || trimmed.Contains("</", StringComparison.Ordinal);
    }

    private static bool LooksLikeHtml(string value) => LooksLikeHtmlForMigration(value);

    private static Converter CreateConverter()
    {
        var config = new Config
        {
            UnknownTags = Config.UnknownTagsOption.Drop,
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true,
        };

        return new Converter(config);
    }
}
