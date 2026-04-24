using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PtoRequest
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string StaffPinId { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string StartDate { get; set; } = null!;

    public string EndDate { get; set; } = null!;

    public double HoursRequested { get; set; }

    public string Status { get; set; } = null!;

    public string? Reason { get; set; }

    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual StaffPin StaffPin { get; set; } = null!;
}
