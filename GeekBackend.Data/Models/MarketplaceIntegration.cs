using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MarketplaceIntegration
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public bool Enabled { get; set; }

    public string? ExternalStoreId { get; set; }

    public string? WebhookSigningSecretEncrypted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<MarketplaceOrder> MarketplaceOrders { get; set; } = new List<MarketplaceOrder>();

    public virtual ICollection<MarketplaceWebhookEvent> MarketplaceWebhookEvents { get; set; } = new List<MarketplaceWebhookEvent>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
