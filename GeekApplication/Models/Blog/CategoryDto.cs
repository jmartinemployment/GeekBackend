namespace GeekApplication.Models.Blog;

public sealed class CategoryDto
{
    public int Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string? Name { get; init; }
}
