using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class ReferralConfig
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public bool Enabled { get; set; }

    public string ReferrerReward { get; set; } = null!;

    public string RefereeReward { get; set; } = null!;

    public int? MaxReferrals { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
