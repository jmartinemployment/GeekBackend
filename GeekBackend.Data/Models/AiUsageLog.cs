using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class AiUsageLog
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string FeatureKey { get; set; } = null!;

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int EstimatedCostCents { get; set; }

    public DateTime CalledAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
