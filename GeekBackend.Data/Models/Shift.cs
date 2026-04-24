using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Shift
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string StaffPinId { get; set; } = null!;

    public DateOnly Date { get; set; }

    public string StartTime { get; set; } = null!;

    public string EndTime { get; set; } = null!;

    public string Position { get; set; } = null!;

    public int BreakMinutes { get; set; }

    public string? Notes { get; set; }

    public bool IsPublished { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual StaffPin StaffPin { get; set; } = null!;

    public virtual ICollection<SwapRequest> SwapRequests { get; set; } = new List<SwapRequest>();
}
