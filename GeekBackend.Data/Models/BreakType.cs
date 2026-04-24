using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class BreakType
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int ExpectedMinutes { get; set; }

    public bool IsPaid { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
