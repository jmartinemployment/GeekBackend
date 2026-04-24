using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class WorkweekConfig
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public int WeekStartDay { get; set; }

    public string DayStartTime { get; set; } = null!;

    public decimal OvertimeThresholdHours { get; set; }

    public decimal OvertimeMultiplier { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
