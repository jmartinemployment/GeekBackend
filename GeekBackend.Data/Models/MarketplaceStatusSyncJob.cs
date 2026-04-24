using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MarketplaceStatusSyncJob
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string MarketplaceOrderId { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public string ExternalOrderId { get; set; } = null!;

    public string TargetStatus { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int AttemptCount { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? LastError { get; set; }

    public string? Payload { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual MarketplaceOrder MarketplaceOrder { get; set; } = null!;

    public virtual Restaurant Restaurant { get; set; } = null!;
}
