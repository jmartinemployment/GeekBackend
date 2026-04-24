using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class LoyaltyTransaction
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string CustomerId { get; set; } = null!;

    public string? OrderId { get; set; }

    public string Type { get; set; } = null!;

    public int Points { get; set; }

    public int BalanceAfter { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual Order? Order { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
