using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CateringCapacitySetting
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public int MaxEventsPerDay { get; set; }

    public int MaxHeadcountPerDay { get; set; }

    public bool ConflictAlertsEnabled { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
