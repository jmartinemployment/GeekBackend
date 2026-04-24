using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PrinterProfile
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public bool IsDefault { get; set; }

    public string RoutingRules { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
