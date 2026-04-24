using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RetailStock
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string RetailItemId { get; set; } = null!;

    public int Quantity { get; set; }

    public int LowStockThreshold { get; set; }

    public string? Location { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual RetailItem RetailItem { get; set; } = null!;
}
