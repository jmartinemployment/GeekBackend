using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class LaborTarget
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public int DayOfWeek { get; set; }

    public decimal TargetPercent { get; set; }

    public decimal? TargetCost { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
