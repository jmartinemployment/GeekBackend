using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class TimecardEditRequest
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string TimeEntryId { get; set; } = null!;

    public string StaffPinId { get; set; } = null!;

    public string EditType { get; set; } = null!;

    public string OriginalValue { get; set; } = null!;

    public string NewValue { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? RespondedBy { get; set; }

    public DateTime? RespondedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual StaffPin StaffPin { get; set; } = null!;

    public virtual TimeEntry TimeEntry { get; set; } = null!;
}
