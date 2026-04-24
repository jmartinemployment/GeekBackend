using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RestaurantLoyaltyConfig
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public bool Enabled { get; set; }

    public int PointsPerDollar { get; set; }

    public decimal PointsRedemptionRate { get; set; }

    public int TierSilverMin { get; set; }

    public int TierGoldMin { get; set; }

    public int TierPlatinumMin { get; set; }

    public decimal SilverMultiplier { get; set; }

    public decimal GoldMultiplier { get; set; }

    public decimal PlatinumMultiplier { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
