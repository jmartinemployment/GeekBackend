namespace GeekApplication.Models.WebPost;

/// <summary>Content-only section — no styling tokens, layout enums, type flags, or CSS helpers.</summary>
public sealed record ContentSectionInput(
    string? HeadingText,
    string BodyContent,
    string? MediaUrl,
    string? MediaAlt);

public sealed record ContentStructureInput(
    IReadOnlyList<ContentSectionInput> Sections,
    string? MainBody);

public sealed record UpsertWebPostCommand(
    string Slug,
    string Title,
    ContentStructureInput ContentStructure);

public sealed record ContentSectionDto(
    string? HeadingText,
    string BodyContent,
    string? MediaUrl,
    string? MediaAlt);

public sealed record ContentStructureDto(
    IReadOnlyList<ContentSectionDto> Sections,
    string? MainBody);

public sealed record WebPostFlatDto(
    Guid Id,
    string Slug,
    string Title,
    ContentStructureDto ContentStructure,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
