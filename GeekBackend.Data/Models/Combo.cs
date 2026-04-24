using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Combo
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public decimal ComboPrice { get; set; }

    public bool IsActive { get; set; }

    public string Items { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
