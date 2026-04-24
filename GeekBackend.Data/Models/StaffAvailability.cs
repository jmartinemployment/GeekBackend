using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class StaffAvailability
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string StaffPinId { get; set; } = null!;

    public int DayOfWeek { get; set; }

    public bool IsAvailable { get; set; }

    public string? PreferredStart { get; set; }

    public string? PreferredEnd { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual StaffPin StaffPin { get; set; } = null!;
}
