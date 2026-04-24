using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OrderCheck
{
    public string Id { get; set; } = null!;

    public string OrderId { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public int DisplayNumber { get; set; }

    public string PaymentStatus { get; set; } = null!;

    public decimal Subtotal { get; set; }

    public decimal Tax { get; set; }

    public decimal Tip { get; set; }

    public decimal Total { get; set; }

    public string? TabName { get; set; }

    public DateTime? TabOpenedAt { get; set; }

    public DateTime? TabClosedAt { get; set; }

    public string? PreauthId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<CheckDiscount> CheckDiscounts { get; set; } = new List<CheckDiscount>();

    public virtual ICollection<CheckItem> CheckItems { get; set; } = new List<CheckItem>();

    public virtual ICollection<CheckVoidedItem> CheckVoidedItems { get; set; } = new List<CheckVoidedItem>();

    public virtual Order Order { get; set; } = null!;
}
