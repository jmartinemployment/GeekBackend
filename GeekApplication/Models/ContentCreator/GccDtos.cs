namespace GeekApplication.Models.ContentCreator;

public sealed record GccCreateDto(
    Guid Id,
    Guid ClientId,
    Guid OwnerUserId,
    string StartingContentType,
    string Topic,
    string? Notes,
    Guid? ProjectSiteRunId,
    string? SiteSectionJson,
    string? BriefJson,
    string? ResearchJson,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string Department = "marketing");

public sealed record CreateGccCreateCommand(
    Guid ClientId,
    Guid OwnerUserId,
    string StartingContentType,
    string Topic,
    string? Notes = null,
    Guid? ProjectSiteRunId = null,
    string? SiteSectionJson = null,
    string? BriefJson = null,
    string? ResearchJson = null,
    string Department = "marketing");

public sealed record UpdateGccCreateBriefResearchCommand(
    string? BriefJson,
    string? ResearchJson);

public sealed record GccArtifactDto(
    Guid Id,
    Guid CreateId,
    Guid? ParentArtifactId,
    string Type,
    string Name,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateGccArtifactCommand(
    Guid CreateId,
    string Type,
    string Name,
    Guid? ParentArtifactId = null);

public sealed record UpdateGccArtifactStatusCommand(
    Guid Id,
    string Status);

public sealed record GccArtifactVersionDto(
    Guid Id,
    Guid ArtifactId,
    int VersionNumber,
    string BodyDocumentJson,
    string? MetadataJson,
    uint RowVersion,
    DateTime CreatedAtUtc);

public sealed record CreateGccArtifactVersionCommand(
    Guid ArtifactId,
    string BodyDocumentJson,
    string? MetadataJson = null);

public sealed record GccApprovalEventDto(
    Guid Id,
    Guid ArtifactVersionId,
    Guid UserId,
    string Action,
    string? Notes,
    DateTime CreatedAtUtc);

public sealed record CreateGccApprovalEventCommand(
    Guid ArtifactVersionId,
    Guid UserId,
    string Action,
    string? Notes = null);

public sealed record GccSiteAnalysisDto(
    Guid Id,
    string Domain,
    string? SeedTopic,
    string GapsJson,
    string Status,
    Guid? SeoProjectId,
    Guid? SeoProfileId,
    string? ErrorMessage,
    string SiteModelJson,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateGccSiteAnalysisCommand(
    Guid? Id,
    string Domain,
    string? SeedTopic,
    string GapsJson,
    string Status = "ready",
    Guid? SeoProjectId = null,
    Guid? SeoProfileId = null,
    string? ErrorMessage = null,
    string? SiteModelJson = null);

public sealed record UpdateGccSiteAnalysisCommand(
    string Status,
    Guid? SeoProjectId,
    Guid? SeoProfileId,
    string? ErrorMessage,
    string? GapsJson,
    string? SiteModelJson);

public sealed record GccSiteFindingDto(
    Guid Id,
    Guid SiteAnalysisId,
    string FindingType,
    string Severity,
    string? AffectedUrl,
    string Title,
    string Summary,
    string DetailsJson,
    DateTime CreatedAtUtc);

public sealed record CreateGccSiteFindingsCommand(
    IReadOnlyList<GccSiteFindingDto> Findings);

/// <summary>
/// A client: who they are, how to reach them, and how they are billed.
/// </summary>
/// <remarks>
/// Contact and billing fields are not optional on the way in — see <see cref="CreateGccClientCommand"/>
/// — but they are ordinary properties here, because this shape is also what a read returns.
/// Rate is nullable on purpose: a client without one cannot have billable time logged against it,
/// which is the correct refusal rather than a gap to fill in with a guess.
/// </remarks>
public sealed record GccClientDto(
    Guid Id,
    string Name,
    string? Notes,
    string ContactName,
    string ContactEmail,
    string? ContactPhone,
    string? BillingContactName,
    string BillingEmail,
    GccClientAddress ContactAddress,
    GccClientAddress BillingAddress,
    int PaymentTermsDays,
    decimal? Rate,
    string Currency,
    string? TaxId,
    string? PoReference,
    GccClientPublishTarget? PublishTarget,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>A postal address. Every part optional — plenty of real clients have only a country.</summary>
public sealed record GccClientAddress(
    string? Line1 = null,
    string? Line2 = null,
    string? City = null,
    string? Region = null,
    string? PostalCode = null,
    string? Country = null)
{
    public static readonly GccClientAddress Empty = new();

    /// <summary>True when nothing was given. Used to store an absent address as null, not as blanks.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Line1)
        && string.IsNullOrWhiteSpace(Line2)
        && string.IsNullOrWhiteSpace(City)
        && string.IsNullOrWhiteSpace(Region)
        && string.IsNullOrWhiteSpace(PostalCode)
        && string.IsNullOrWhiteSpace(Country);
}

/// <summary>
/// Per-client GeekBackend publish configuration.
/// </summary>
/// <remarks>
/// <see cref="ClientIdEnvVar"/> and <see cref="ClientSecretEnvVar"/> name environment variables the
/// publish service reads at call time. The secrets themselves are never stored here, and that is
/// what makes this safe to keep on the client row at all.
/// </remarks>
public sealed record GccClientPublishTarget(
    string ApiBaseUrl,
    string OAuthTokenEndpoint,
    string ClientIdEnvVar,
    string ClientSecretEnvVar,
    int? DefaultAuthorId,
    string? CategoryStrategy);

/// <summary>
/// Create a client. Contact and billing are required because a client that cannot be invoiced is
/// not a client; rate is not, because a missing rate should stop billable time rather than be
/// invented.
/// </summary>
public sealed record CreateGccClientCommand(
    string Name,
    string ContactName,
    string ContactEmail,
    string BillingEmail,
    int PaymentTermsDays,
    string Currency,
    string? Notes = null,
    string? ContactPhone = null,
    string? BillingContactName = null,
    GccClientAddress? ContactAddress = null,
    GccClientAddress? BillingAddress = null,
    decimal? Rate = null,
    string? TaxId = null,
    string? PoReference = null,
    GccClientPublishTarget? PublishTarget = null);

/// <summary>Update everything about a client except its identity.</summary>
public sealed record UpdateGccClientCommand(
    Guid Id,
    string Name,
    string ContactName,
    string ContactEmail,
    string BillingEmail,
    int PaymentTermsDays,
    string Currency,
    string? Notes = null,
    string? ContactPhone = null,
    string? BillingContactName = null,
    GccClientAddress? ContactAddress = null,
    GccClientAddress? BillingAddress = null,
    decimal? Rate = null,
    string? TaxId = null,
    string? PoReference = null,
    GccClientPublishTarget? PublishTarget = null);
