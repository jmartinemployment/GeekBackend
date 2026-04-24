using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MarketplaceMenuMapping
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public string ExternalItemId { get; set; } = null!;

    public string? ExternalItemName { get; set; }

    public string MenuItemId { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual MenuItem MenuItem { get; set; } = null!;

    public virtual Restaurant Restaurant { get; set; } = null!;
}
