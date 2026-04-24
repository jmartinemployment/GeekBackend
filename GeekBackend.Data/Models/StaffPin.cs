using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class StaffPin
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Pin { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Role { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? TeamMemberId { get; set; }

    public virtual ICollection<PtoRequest> PtoRequests { get; set; } = new List<PtoRequest>();

    public virtual ICollection<Shift> Shifts { get; set; } = new List<Shift>();

    public virtual ICollection<StaffAvailability> StaffAvailabilities { get; set; } = new List<StaffAvailability>();

    public virtual ICollection<SwapRequest> SwapRequests { get; set; } = new List<SwapRequest>();

    public virtual TeamMember? TeamMember { get; set; }

    public virtual ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();

    public virtual ICollection<TimecardEditRequest> TimecardEditRequests { get; set; } = new List<TimecardEditRequest>();
}
