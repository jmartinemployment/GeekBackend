namespace GeekApplication.Models.ContentWriterV4;

public sealed record TemplateFieldSchema(
    string Key,
    string Label,
    string Type,
    string? Placeholder,
    bool Required,
    IReadOnlyList<string>? Options,
    string? Default);
