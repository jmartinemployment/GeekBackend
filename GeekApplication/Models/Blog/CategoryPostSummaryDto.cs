namespace GeekApplication.Models.Blog;

/// <summary>Category + title + one summary variant, for category-grouped listing pages (Home, Use-Case, Tools, Blog summary).</summary>
public sealed class CategoryPostSummaryDto
{
    public int PostId { get; init; }
    public int CategoryId { get; init; }
    public string CategorySlug { get; init; } = string.Empty;
    public string? CategoryName { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}
