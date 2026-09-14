using GeekApplication.Models.Blog;

namespace GeekAPI.Services.ContentCreatorV2.Publish;

/// <summary>
/// CMS category preflight — preferred slug must exist. No blog-like / first-category substitute.
/// </summary>
public static class GccV2CmsCategoryGate
{
    public static string ResolveOrThrow(string preferredSlug, IReadOnlyList<CategoryDto> categories)
    {
        var preferred = (preferredSlug ?? "").Trim();
        if (string.IsNullOrWhiteSpace(preferred))
            throw new InvalidOperationException("Category slug is required before CMS publish.");

        if (categories.Count == 0)
            throw new InvalidOperationException(
                "geek_blog.categories is empty — seed at least one category before publishing.");

        if (categories.Any(c => string.Equals(c.Slug, preferred, StringComparison.OrdinalIgnoreCase)))
            return preferred;

        throw new InvalidOperationException(
            $"Category slug '{preferred}' was not found in geek_blog.categories. "
            + "Seed that category or choose an existing slug before publishing.");
    }
}
