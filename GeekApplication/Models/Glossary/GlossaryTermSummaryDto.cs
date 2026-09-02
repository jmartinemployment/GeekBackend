namespace GeekApplication.Models.Glossary;

public sealed class GlossaryTermSummaryDto
{
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? ShortSummary { get; init; }
}
