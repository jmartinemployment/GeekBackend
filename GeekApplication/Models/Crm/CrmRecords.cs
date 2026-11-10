namespace GeekApplication.Models.Crm;

// What GeekRepository returns and accepts on repo/crm/*. JSON camelCase on the wire.

public sealed record CrmContactPoint(
    Guid Id,
    string Kind,
    string Label,
    string Value,
    bool IsPrimary,
    bool DoNotContact,
    bool SmsOptOut,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? Region = null,
    string? PostalCode = null,
    string? Country = null);

public sealed record CrmPersonDetails(
    string? FirstName,
    string? LastName,
    string? PreferredName,
    string? JobTitle,
    DateOnly? BirthDate = null,
    string? Gender = null,
    string? MaritalStatus = null,
    int? DependentsCount = null,
    string? EducationLevel = null,
    string? Occupation = null);

public sealed record CrmOrganizationDetails(
    string LegalName,
    string? Dba,
    string? OrganizationType,
    string? NaicsCode,
    string? SicCode,
    string? IndustryLabel,
    int? EmployeeCount,
    int? YearStarted,
    string? AnnualRevenueBand,
    string? Website,
    string? ServiceArea,
    string? Description);

public sealed record CrmLink(Guid Id, Guid FromParticipantId, Guid ToParticipantId, string Role, bool IsPrimary, string OtherDisplayName);

public sealed record CrmActivity(
    Guid Id,
    Guid? ParticipantId,
    Guid? DealId,
    string Channel,
    string? Direction,
    DateTime OccurredAt,
    string? Subject,
    string? Body,
    string? Summary,
    string? RecordingUrl,
    int? DurationSeconds,
    string? Outcome,
    string? ExternalRef,
    string CreatedBy);

public sealed record CrmParticipant(
    Guid Id,
    Guid TenantId,
    string Kind,
    string DisplayName,
    string LifecycleStage,
    string Source,
    string? SourceDetail,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    CrmPersonDetails? Person,
    CrmOrganizationDetails? Organization,
    IReadOnlyList<CrmContactPoint> ContactPoints,
    IReadOnlyList<CrmLink> Links,
    IReadOnlyList<CrmActivity> RecentActivities);

/// <summary>A participant row for lists: no children.</summary>
public sealed record CrmParticipantSummary(
    Guid Id,
    string Kind,
    string DisplayName,
    string LifecycleStage,
    string Source,
    string? PrimaryPhone,
    string? PrimaryEmail,
    DateTime CreatedAt,
    DateTime? LastActivityAt);

public sealed record CrmDeal(
    Guid Id,
    string Name,
    Guid PipelineId,
    Guid StageId,
    string StageName,
    string StageOutcome,
    Guid? OrganizationParticipantId,
    string ProductCategory,
    string? QualificationJson,
    decimal? EstimatedValue,
    decimal? RecurringValueMonthly,
    DateOnly? ExpectedCloseOn,
    DateTime? ClosedAt,
    string? LostReason,
    DateTime CreatedAt);

// ----- Commands -----

/// <summary>
/// Find the participant who owns this phone number, or create a person lead for it. The receptionist
/// calls this for every caller; a second call from the same number finds the same record.
/// </summary>
public sealed record EnsureParticipantByPhoneCommand(Guid TenantId, string PhoneE164, string Source, string? SourceDetail);

public sealed record EnsureParticipantResult(Guid ParticipantId, bool Created);

/// <summary>Idempotent on (TenantId, ExternalRef) when ExternalRef is set: a Twilio retry records nothing new.</summary>
public sealed record RecordActivityCommand(
    Guid TenantId,
    Guid? ParticipantId,
    Guid? DealId,
    string Channel,
    string? Direction,
    DateTime OccurredAt,
    string? Subject,
    string? Body,
    string? Summary,
    string? RecordingUrl,
    int? DurationSeconds,
    string? Outcome,
    string? ExternalRef,
    string CreatedBy);

public sealed record RecordActivityResult(Guid ActivityId, bool Created);

public sealed record SetSmsOptOutCommand(Guid TenantId, string PhoneE164, bool OptedOut);

/// <summary>One prospect from a list (Apollo, a spreadsheet): a person, their business, or both.</summary>
public sealed record ImportProspect(
    string? FirstName,
    string? LastName,
    string? JobTitle,
    string? Email,
    IReadOnlyList<ImportPhone> Phones,
    string? City,
    string? Region,
    string? Country,
    string? LinkedInUrl,
    string? CompanyName,
    string? CompanyWebsite,
    string? CompanyPhoneE164,
    string? CompanyAddressLine1,
    string? CompanyCity,
    string? CompanyRegion,
    string? CompanyPostalCode,
    string? CompanyCountry,
    string? IndustryLabel,
    string? NaicsCode,
    string? SicCode,
    int? EmployeeCount,
    string? AnnualRevenueBand,
    string? PersonRoleAtCompany,
    bool DoNotCall,
    string? ExternalId);

public sealed record ImportPhone(string E164, string Label);

public sealed record ImportProspectsCommand(Guid TenantId, string SourceDetail, IReadOnlyList<ImportProspect> Prospects);

public sealed record ImportProspectsResult(int PeopleCreated, int OrganizationsCreated, int Matched, int Skipped, IReadOnlyList<string> Problems);

public sealed record CreateDealCommand(
    Guid TenantId,
    string Name,
    string ProductCategory,
    Guid? OrganizationParticipantId,
    Guid? PrimaryContactParticipantId,
    string? QualificationJson,
    decimal? EstimatedValue,
    decimal? RecurringValueMonthly,
    string? StageName);

public sealed record MoveDealStageCommand(Guid TenantId, string StageName, string? LostReason, string ChangedBy);
