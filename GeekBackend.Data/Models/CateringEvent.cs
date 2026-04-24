using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CateringEvent
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string EventType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateOnly FulfillmentDate { get; set; }

    public string StartTime { get; set; } = null!;

    public string EndTime { get; set; } = null!;

    public int Headcount { get; set; }

    public string LocationType { get; set; } = null!;

    public string? LocationAddress { get; set; }

    public string ClientName { get; set; } = null!;

    public string? ClientPhone { get; set; }

    public string? ClientEmail { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateOnly BookingDate { get; set; }

    public string? BrandingColor { get; set; }

    public string? BrandingLogoUrl { get; set; }

    public string? CompanyName { get; set; }

    public DateTime? ContractSignedAt { get; set; }

    public string? ContractUrl { get; set; }

    public string? DeliveryDetails { get; set; }

    public string? DietaryRequirements { get; set; }

    public string? EstimateId { get; set; }

    public int GratuityCents { get; set; }

    public decimal? GratuityPercent { get; set; }

    public string? InvoiceId { get; set; }

    public string? InvoiceNotes { get; set; }

    public string Milestones { get; set; } = null!;

    public string Packages { get; set; } = null!;

    public int PaidCents { get; set; }

    public string? SelectedPackageId { get; set; }

    public int ServiceChargeCents { get; set; }

    public decimal? ServiceChargePercent { get; set; }

    public int SubtotalCents { get; set; }

    public string? Tastings { get; set; }

    public int TaxCents { get; set; }

    public decimal? TaxPercent { get; set; }

    public int TotalCents { get; set; }

    public DateTime? ProposalSentAt { get; set; }

    public string? SignatureImageUrl { get; set; }

    public DateTime? SignerConsentedAt { get; set; }

    public string? SignerIp { get; set; }

    public string? AiContent { get; set; }

    public virtual ICollection<CateringActivity> CateringActivities { get; set; } = new List<CateringActivity>();

    public virtual ICollection<CateringProposalToken> CateringProposalTokens { get; set; } = new List<CateringProposalToken>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
