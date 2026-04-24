using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CycleCountItem
{
    public string Id { get; set; } = null!;

    public string CycleCountId { get; set; } = null!;

    public string InventoryItemId { get; set; } = null!;

    public decimal ExpectedQty { get; set; }

    public decimal? ActualQty { get; set; }

    public decimal? Variance { get; set; }

    public virtual CycleCount CycleCount { get; set; } = null!;
}
