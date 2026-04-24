using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MarketplaceWebhookEvent
{
    public string Id { get; set; } = null!;

    public string? RestaurantId { get; set; }

    public string? IntegrationId { get; set; }

    public string Provider { get; set; } = null!;

    public string ExternalEventId { get; set; } = null!;

    public string? ExternalOrderId { get; set; }

    public string PayloadHash { get; set; } = null!;

    public string? Payload { get; set; }

    public string Outcome { get; set; } = null!;

    public string? ErrorMessage { get; set; }

    public DateTime ReceivedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public virtual MarketplaceIntegration? Integration { get; set; }

    public virtual Restaurant? Restaurant { get; set; }
}
