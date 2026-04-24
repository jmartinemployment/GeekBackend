using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Modifier
{
    public string Id { get; set; } = null!;

    public string ModifierGroupId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? NameEn { get; set; }

    public decimal PriceAdjustment { get; set; }

    public bool IsDefault { get; set; }

    public bool Available { get; set; }

    public int DisplayOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ModifierGroup ModifierGroup { get; set; } = null!;

    public virtual ICollection<OrderItemModifier> OrderItemModifiers { get; set; } = new List<OrderItemModifier>();
}
