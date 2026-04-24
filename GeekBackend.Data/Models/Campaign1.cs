using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Campaign1
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Channel { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? Subject { get; set; }

    public string Body { get; set; } = null!;

    public string? AudienceSegment { get; set; }

    public string? AudienceLoyaltyTier { get; set; }

    public int? EstimatedRecipients { get; set; }

    public DateTime? ScheduledAt { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual CampaignPerformance? CampaignPerformance { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
