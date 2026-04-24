using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class LoyaltyReward
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public int PointsCost { get; set; }

    public string DiscountType { get; set; } = null!;

    public decimal DiscountValue { get; set; }

    public string MinTier { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
