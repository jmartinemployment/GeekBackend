using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class TemplateShift
{
    public string Id { get; set; } = null!;

    public string TemplateId { get; set; } = null!;

    public string StaffPinId { get; set; } = null!;

    public string StaffName { get; set; } = null!;

    public int DayOfWeek { get; set; }

    public string StartTime { get; set; } = null!;

    public string EndTime { get; set; } = null!;

    public string Position { get; set; } = null!;

    public int BreakMinutes { get; set; }

    public virtual ScheduleTemplate Template { get; set; } = null!;
}
