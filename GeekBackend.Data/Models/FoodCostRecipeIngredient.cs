using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class FoodCostRecipeIngredient
{
    public string Id { get; set; } = null!;

    public string RecipeId { get; set; } = null!;

    public string IngredientName { get; set; } = null!;

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = null!;

    public decimal EstimatedUnitCost { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual FoodCostRecipe Recipe { get; set; } = null!;
}
