namespace GeekApplication.Models.Glossary;

public sealed record GlossaryTermDto
{
    public int Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? ShortSummary { get; init; }
    public string Status { get; init; } = "published";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public IReadOnlyList<GlossaryDefinitionDto> Definitions { get; init; } = [];
}
