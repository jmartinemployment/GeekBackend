namespace GeekApplication.Models.ContentWriterV4;

public sealed record GenerationDto(
    Guid Id,
    Guid? DocumentId,
    Guid TemplateId,
    Guid? BrandVoiceId,
    string Provider,
    string Model,
    IReadOnlyDictionary<string, string> Inputs,
    string Output,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    DateTime CreatedAtUtc);

public sealed record CreateGenerationCommand(
    Guid? DocumentId,
    Guid TemplateId,
    Guid? BrandVoiceId,
    string Provider,
    string Model,
    IReadOnlyDictionary<string, string> Inputs,
    string Output,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd);

public sealed record UsageSummaryDto(
    int TotalGenerations,
    int TotalInputTokens,
    int TotalOutputTokens,
    decimal TotalCostUsd,
    IReadOnlyList<ProviderUsageDto> ByProvider);

public sealed record ProviderUsageDto(
    string Provider,
    int Generations,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd);
