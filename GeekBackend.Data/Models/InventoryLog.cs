using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class InventoryLog
{
    public string Id { get; set; } = null!;

    public string InventoryItemId { get; set; } = null!;

    public decimal PreviousStock { get; set; }

    public decimal NewStock { get; set; }

    public decimal ChangeAmount { get; set; }

    public string? Reason { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual InventoryItem InventoryItem { get; set; } = null!;
}
