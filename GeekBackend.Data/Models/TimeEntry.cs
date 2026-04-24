using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class TimeEntry
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string StaffPinId { get; set; } = null!;

    public string? ShiftId { get; set; }

    public DateTime ClockIn { get; set; }

    public DateTime? ClockOut { get; set; }

    public int BreakMinutes { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual StaffPin StaffPin { get; set; } = null!;

    public virtual ICollection<TimecardEditRequest> TimecardEditRequests { get; set; } = new List<TimecardEditRequest>();
}
