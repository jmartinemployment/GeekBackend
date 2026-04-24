using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OrderSentiment
{
    public string Id { get; set; } = null!;

    public string OrderId { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string OrderNumber { get; set; } = null!;

    public string? TableNumber { get; set; }

    public string Sentiment { get; set; } = null!;

    public List<string>? Flags { get; set; }

    public string Urgency { get; set; } = null!;

    public string Summary { get; set; } = null!;

    public bool IsRead { get; set; }

    public DateTime AnalyzedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
