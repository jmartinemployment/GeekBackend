namespace GeekApplication.Models.ContentWriterV4;

public sealed record DocumentDto(
    Guid Id,
    Guid OwnerId,
    Guid? TemplateId,
    Guid? BrandVoiceId,
    string Title,
    IReadOnlyDictionary<string, string> Inputs,
    string Content,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateDocumentCommand(
    Guid OwnerId,
    Guid? TemplateId,
    Guid? BrandVoiceId,
    string Title,
    IReadOnlyDictionary<string, string> Inputs,
    string Content);

public sealed record UpdateDocumentCommand(
    Guid Id,
    string Title,
    string Content);
