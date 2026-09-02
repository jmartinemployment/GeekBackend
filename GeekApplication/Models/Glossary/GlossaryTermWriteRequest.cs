namespace GeekApplication.Models.Glossary;

public sealed class GlossaryTermWriteRequest
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? ShortSummary { get; set; }
    public string Status { get; set; } = "published";
    public IReadOnlyList<GlossaryDefinitionWriteRequest> Definitions { get; set; } = [];
}

public sealed class GlossaryDefinitionWriteRequest
{
    public int SortOrder { get; set; }
    public string PartOfSpeech { get; set; } = "noun";
    public string Text { get; set; } = string.Empty;
    public string? Example { get; set; }
}
