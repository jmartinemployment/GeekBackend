using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OrderItem
{
    public string Id { get; set; } = null!;

    public string OrderId { get; set; } = null!;

    public string? MenuItemId { get; set; }

    public string MenuItemName { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TotalPrice { get; set; }

    public string? SpecialInstructions { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public decimal ModifiersPrice { get; set; }

    public DateTime? SentToKitchenAt { get; set; }

    public string Status { get; set; } = null!;

    public string? CourseFireStatus { get; set; }

    public DateTime? CourseFiredAt { get; set; }

    public string? CourseGuid { get; set; }

    public string? CourseName { get; set; }

    public int? CourseSortOrder { get; set; }

    public string FulfillmentStatus { get; set; } = null!;

    public DateTime? CourseReadyAt { get; set; }

    public virtual MenuItem? MenuItem { get; set; }

    public virtual Order Order { get; set; } = null!;

    public virtual ICollection<OrderItemModifier> OrderItemModifiers { get; set; } = new List<OrderItemModifier>();
}
