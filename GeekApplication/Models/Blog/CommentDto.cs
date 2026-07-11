namespace GeekApplication.Models.Blog;

public sealed class CommentDto
{
    public int Id { get; init; }
    public int PostId { get; init; }
    public int? UserId { get; init; }
    public string Content { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public int Depth { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
