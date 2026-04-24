using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Subscription
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int PlanPrice { get; set; }

    public DateTime? CurrentPeriodStart { get; set; }

    public DateTime? CurrentPeriodEnd { get; set; }

    public DateTime? CanceledAt { get; set; }

    public bool CancelAtPeriodEnd { get; set; }

    public string? PaypalSubscriptionId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
