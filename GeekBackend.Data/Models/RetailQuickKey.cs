using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RetailQuickKey
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Label { get; set; } = null!;

    public string? RetailItemId { get; set; }

    public int Position { get; set; }

    public string? Color { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
