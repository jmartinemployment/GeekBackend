namespace GeekApplication.Models.ContentWriterV3;

public sealed record WorkspaceDto(
    Guid Id,
    Guid OwnerId,
    string Name,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateWorkspaceCommand(
    string Name,
    Guid OwnerId,
    Guid? Id = null);

public sealed record UpdateWorkspaceCommand(
    Guid Id,
    string Name);

public sealed record ClientDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateClientCommand(
    Guid WorkspaceId,
    string Name);

public sealed record ClientProfileDto(
    Guid Id,
    Guid ClientId,
    string Name,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateClientProfileCommand(
    Guid ClientId,
    string Name);

public sealed record ClientProfileVersionDto(
    Guid Id,
    Guid ProfileId,
    int Version,
    Dictionary<string, object> ApprovedFacts,
    Dictionary<string, object> ProhibitedClaims,
    DateTime CreatedAtUtc,
    uint RowVersion);

public sealed record CreateClientProfileVersionCommand(
    Guid ProfileId,
    Dictionary<string, object> ApprovedFacts,
    Dictionary<string, object> ProhibitedClaims);

public sealed record ClientBrandVoiceLinkDto(
    Guid Id,
    Guid ProfileVersionId,
    Guid BrandVoiceId,
    DateTime CreatedAtUtc);

public sealed record CreateClientBrandVoiceLinkCommand(
    Guid ProfileVersionId,
    Guid BrandVoiceId);
