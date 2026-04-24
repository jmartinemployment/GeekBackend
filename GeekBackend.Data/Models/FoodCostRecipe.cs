using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class FoodCostRecipe
{
    public string Id { get; set; } = null!;

    public string MenuItemId { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public decimal YieldQty { get; set; }

    public string YieldUnit { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<FoodCostRecipeIngredient> FoodCostRecipeIngredients { get; set; } = new List<FoodCostRecipeIngredient>();

    public virtual MenuItem MenuItem { get; set; } = null!;

    public virtual Restaurant Restaurant { get; set; } = null!;
}
