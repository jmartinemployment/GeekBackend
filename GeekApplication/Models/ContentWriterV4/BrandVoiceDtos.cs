namespace GeekApplication.Models.ContentWriterV4;

public sealed record BrandVoiceDto(
    Guid Id,
    Guid OwnerId,
    string Name,
    string Description,
    string Tone,
    string SampleText,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateBrandVoiceCommand(
    Guid OwnerId,
    string Name,
    string Description,
    string Tone,
    string SampleText);

public sealed record UpdateBrandVoiceCommand(
    Guid Id,
    string Name,
    string Description,
    string Tone,
    string SampleText);
