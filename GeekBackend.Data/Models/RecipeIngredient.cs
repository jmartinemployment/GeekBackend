using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RecipeIngredient
{
    public string Id { get; set; } = null!;

    public string MenuItemId { get; set; } = null!;

    public string InventoryItemId { get; set; } = null!;

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = null!;

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual InventoryItem InventoryItem { get; set; } = null!;

    public virtual MenuItem MenuItem { get; set; } = null!;
}
