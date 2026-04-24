using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CheckVoidedItem
{
    public string Id { get; set; } = null!;

    public string CheckId { get; set; } = null!;

    public string CheckItemId { get; set; } = null!;

    public string OrderId { get; set; } = null!;

    public string MenuItemName { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalPrice { get; set; }

    public string VoidReason { get; set; } = null!;

    public string VoidedBy { get; set; } = null!;

    public string? ManagerApproval { get; set; }

    public DateTime VoidedAt { get; set; }

    public virtual OrderCheck Check { get; set; } = null!;
}
