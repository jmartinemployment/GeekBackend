using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OrderItemModifier
{
    public string Id { get; set; } = null!;

    public string OrderItemId { get; set; } = null!;

    public string? ModifierId { get; set; }

    public string ModifierName { get; set; } = null!;

    public decimal PriceAdjustment { get; set; }

    public virtual Modifier? Modifier { get; set; }

    public virtual OrderItem OrderItem { get; set; } = null!;
}
