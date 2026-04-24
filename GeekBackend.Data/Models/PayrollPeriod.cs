using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PayrollPeriod
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string Status { get; set; } = null!;

    public string Summaries { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
