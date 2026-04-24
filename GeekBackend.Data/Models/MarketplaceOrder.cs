using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MarketplaceOrder
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? OrderId { get; set; }

    public string? IntegrationId { get; set; }

    public string Provider { get; set; } = null!;

    public string ExternalOrderId { get; set; } = null!;

    public string? ExternalStoreId { get; set; }

    public string? ExternalCustomerId { get; set; }

    public string Status { get; set; } = null!;

    public string? RawPayload { get; set; }

    public string? LastEventId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? LastPushAt { get; set; }

    public string? LastPushError { get; set; }

    public string? LastPushResult { get; set; }

    public string? LastPushedStatus { get; set; }

    public virtual MarketplaceIntegration? Integration { get; set; }

    public virtual ICollection<MarketplaceStatusSyncJob> MarketplaceStatusSyncJobs { get; set; } = new List<MarketplaceStatusSyncJob>();

    public virtual Order? Order { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
