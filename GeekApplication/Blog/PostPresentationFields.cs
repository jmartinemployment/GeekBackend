namespace GeekApplication.Blog;

/// <summary>
/// Per-post presentation fields stored on geek_blog.post_translations.
/// Only the column matching the slug prefix should be set for a given post.
/// </summary>
public sealed record PostPresentationFields(
    string? BlogExcerpt,
    string? TechnicalArticleExcerpt,
    string? ToolExcerpt,
    string? AdvertisingExcerpt,
    string? HeroImageUrl)
{
    public static PostPresentationFields Empty { get; } = new(null, null, null, null, null);

    public static PostPresentationFields ForSlug(string apiSlug, string? listingExcerpt, string? heroImageUrl = null)
    {
        var excerpt = string.IsNullOrWhiteSpace(listingExcerpt) ? null : listingExcerpt.Trim();
        var hero = string.IsNullOrWhiteSpace(heroImageUrl) ? null : heroImageUrl.Trim();
        var normalized = apiSlug.Trim().Trim('/');

        if (normalized.StartsWith("tools/", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, null, excerpt, null, hero);
        }

        if (normalized.StartsWith("use-cases/", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, excerpt, null, null, hero);
        }

        if (normalized.StartsWith("blog/", StringComparison.OrdinalIgnoreCase))
        {
            return new(excerpt, null, null, null, hero);
        }

        return new(excerpt, null, null, null, hero);
    }

    public static PostPresentationFields FromRequest(
        string? blogExcerpt,
        string? technicalArticleExcerpt,
        string? toolExcerpt,
        string? advertisingExcerpt,
        string? heroImageUrl) =>
        new(
            Normalize(blogExcerpt),
            Normalize(technicalArticleExcerpt),
            Normalize(toolExcerpt),
            Normalize(advertisingExcerpt),
            Normalize(heroImageUrl));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
