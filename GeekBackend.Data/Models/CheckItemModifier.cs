using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CheckItemModifier
{
    public string Id { get; set; } = null!;

    public string CheckItemId { get; set; } = null!;

    public string? ModifierId { get; set; }

    public string ModifierName { get; set; } = null!;

    public decimal PriceAdjustment { get; set; }

    public virtual CheckItem CheckItem { get; set; } = null!;
}
