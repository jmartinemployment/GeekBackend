using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class SwapRequest
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string ShiftId { get; set; } = null!;

    public string RequestorPinId { get; set; } = null!;

    public string? TargetPinId { get; set; }

    public string Reason { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? RespondedAt { get; set; }

    public string? RespondedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual StaffPin RequestorPin { get; set; } = null!;

    public virtual Shift Shift { get; set; } = null!;
}
