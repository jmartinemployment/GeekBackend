using GeekApplication.Models.Crm;

namespace GeekRepository.Data.Entities.Crm;

// One class per table of the geek_crm schema (Migrations/Sql/0050_geek_crm_schema.sql).
// CrmDbContext names every column as the snake_case of its property. Instants are UTC.

public sealed class CrmTenantRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class CrmParticipantRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LifecycleStage { get; set; } = "lead";
    public string Source { get; set; } = string.Empty;
    public string? SourceDetail { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? PreferredLanguage { get; set; }
    public string? Notes { get; set; }
    public string? ExternalId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public Guid? ImportBatchId { get; set; }
}

public sealed class CrmPersonRow
{
    public Guid ParticipantId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PreferredName { get; set; }
    public string? JobTitle { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Gender { get; set; }
    public string? MaritalStatus { get; set; }
    public int? DependentsCount { get; set; }
    public string? EducationLevel { get; set; }
    public string? Occupation { get; set; }
}

public sealed class CrmOrganizationRow
{
    public Guid ParticipantId { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string? Dba { get; set; }
    public string? OrganizationType { get; set; }
    public string? NaicsCode { get; set; }
    public string? SicCode { get; set; }
    public string? IndustryLabel { get; set; }
    public int? EmployeeCount { get; set; }
    public int? YearStarted { get; set; }
    public string? AnnualRevenueBand { get; set; }
    public string? Website { get; set; }
    public string? ServiceArea { get; set; }
    public string? Description { get; set; }
}

public sealed class CrmContactPointRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ParticipantId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = "other";
    public string Value { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public bool IsPrimary { get; set; }
    public bool DoNotContact { get; set; }
    public bool SmsOptOut { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ImportBatchId { get; set; }
}

public sealed class CrmParticipantLinkRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid FromParticipantId { get; set; }
    public Guid ToParticipantId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateOnly? StartedOn { get; set; }
    public DateOnly? EndedOn { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CrmPipelineRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CrmStageRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PipelineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public string Outcome { get; set; } = "open";
    public int? ProbabilityPct { get; set; }
}

public sealed class CrmDealRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid PipelineId { get; set; }
    public Guid StageId { get; set; }
    public Guid? OrganizationParticipantId { get; set; }
    public string ProductCategory { get; set; } = string.Empty;
    public string? Qualification { get; set; }
    public decimal? EstimatedValue { get; set; }
    public decimal? RecurringValueMonthly { get; set; }
    public DateOnly? ExpectedCloseOn { get; set; }
    public Guid? OwnerUserId { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? LostReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CrmDealStageHistoryRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DealId { get; set; }
    public Guid? FromStageId { get; set; }
    public Guid ToStageId { get; set; }
    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
}

public sealed class CrmDealParticipantRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DealId { get; set; }
    public Guid ParticipantId { get; set; }
    public string Role { get; set; } = string.Empty;
}

public sealed class CrmActivityRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ParticipantId { get; set; }
    public Guid? DealId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string? Direction { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public string? Summary { get; set; }
    public string? Transcript { get; set; }
    public string? RecordingUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Outcome { get; set; }
    public string? ExternalRef { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class CrmPropertyDefinitionRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string? Options { get; set; }
    public bool IsSensitive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RetiredAt { get; set; }
}

public sealed class CrmPropertyValueRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DefinitionId { get; set; }
    public Guid RecordId { get; set; }
    public string Value { get; set; } = "null";
    public DateTime UpdatedAt { get; set; }
}

public sealed class CrmNaicsCodeRow
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Level { get; set; }
}

public sealed class CrmProductRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? UseCasePath { get; set; }
    public Guid? PartnerParticipantId { get; set; }
    public string Billing { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CrmProductPriceRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public string? Size { get; set; }
    public decimal? Hours { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? RecurringPriceMonthly { get; set; }
    public string Currency { get; set; } = Currencies.Usd;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CrmPricingRulesRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public decimal? HourlyRate { get; set; }
    public decimal RelatedUseCasePct { get; set; } = 60m;
    public string Currency { get; set; } = Currencies.Usd;
    public DateTime UpdatedAt { get; set; }
}

public sealed class CrmPricingUpliftTierRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public decimal BookedPctFrom { get; set; }
    public decimal UpliftPct { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CrmDealLineItemRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DealId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsRecurring { get; set; }
    public decimal DiscountPct { get; set; }
    public string Currency { get; set; } = Currencies.Usd;
    public DateTime CreatedAt { get; set; }
}

public sealed class CrmCustomerHoldingRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ParticipantId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? DealLineItemId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly? StartOn { get; set; }
    public DateOnly? EndOn { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal? RecurringAmountMonthly { get; set; }
    public decimal? OneTimeAmount { get; set; }
    public string Currency { get; set; } = Currencies.Usd;
    public string? Arrangement { get; set; }
    public decimal? ReferralFee { get; set; }
    public bool? ReferralFeeIsRecurring { get; set; }
    public DateOnly? ReferralPaidOn { get; set; }
    public decimal? PartnerCostMonthly { get; set; }
    public string? ExternalRef { get; set; }
    public Guid? TenantSubscriptionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CrmTenantSubscriptionRow
{
    public Guid Id { get; set; }
    public Guid SubscriberTenantId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string? Plan { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly StartOn { get; set; }
    public DateOnly? EndOn { get; set; }
    public DateTime CreatedAt { get; set; }
}

