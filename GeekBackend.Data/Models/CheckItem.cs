using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CheckItem
{
    public string Id { get; set; } = null!;

    public string CheckId { get; set; } = null!;

    public string OrderId { get; set; } = null!;

    public string? MenuItemId { get; set; }

    public string MenuItemName { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal ModifiersPrice { get; set; }

    public decimal TotalPrice { get; set; }

    public string? SpecialInstructions { get; set; }

    public int? SeatNumber { get; set; }

    public string FulfillmentStatus { get; set; } = null!;

    public string? CourseGuid { get; set; }

    public bool IsComped { get; set; }

    public string? CompReason { get; set; }

    public string? CompBy { get; set; }

    public string? CompApprovedBy { get; set; }

    public DateTime? CompAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual OrderCheck Check { get; set; } = null!;

    public virtual ICollection<CheckItemModifier> CheckItemModifiers { get; set; } = new List<CheckItemModifier>();
}
