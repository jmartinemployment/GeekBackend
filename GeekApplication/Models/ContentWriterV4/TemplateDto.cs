namespace GeekApplication.Models.ContentWriterV4;

public sealed record TemplateDto(
    Guid Id,
    string Slug,
    string Name,
    string Description,
    string Category,
    string Icon,
    IReadOnlyList<TemplateFieldSchema> InputSchema,
    string SystemPrompt,
    string UserPromptTemplate,
    bool IsActive,
    DateTime CreatedAtUtc);
