using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class UnitConversion
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string FromUnit { get; set; } = null!;

    public string ToUnit { get; set; } = null!;

    public decimal Factor { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
