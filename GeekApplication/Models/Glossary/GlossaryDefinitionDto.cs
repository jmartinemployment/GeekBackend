namespace GeekApplication.Models.Glossary;

public sealed class GlossaryDefinitionDto
{
    public int SortOrder { get; init; }
    public string PartOfSpeech { get; init; } = "noun";
    public string Text { get; init; } = string.Empty;
    public string? Example { get; init; }
}
