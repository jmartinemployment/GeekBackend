using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class InventoryItem
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? NameEn { get; set; }

    public string Unit { get; set; } = null!;

    public decimal CurrentStock { get; set; }

    public decimal MinStock { get; set; }

    public decimal MaxStock { get; set; }

    public decimal CostPerUnit { get; set; }

    public string? Supplier { get; set; }

    public string Category { get; set; } = null!;

    public DateTime? LastRestocked { get; set; }

    public DateTime? LastCountDate { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ExpirationDate { get; set; }

    public virtual ICollection<InventoryLog> InventoryLogs { get; set; } = new List<InventoryLog>();

    public virtual ICollection<RecipeIngredient> RecipeIngredients { get; set; } = new List<RecipeIngredient>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
