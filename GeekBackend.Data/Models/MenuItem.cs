using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MenuItem
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string CategoryId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public decimal Price { get; set; }

    public decimal? Cost { get; set; }

    public string? Image { get; set; }

    public bool Available { get; set; }

    public bool EightySixed { get; set; }

    public string? EightySixReason { get; set; }

    public bool Popular { get; set; }

    public List<string>? Dietary { get; set; }

    public int DisplayOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? DescriptionEn { get; set; }

    public string? AiConfidence { get; set; }

    public decimal? AiEstimatedCost { get; set; }

    public DateTime? AiLastUpdated { get; set; }

    public decimal? AiProfitMargin { get; set; }

    public decimal? AiSuggestedPrice { get; set; }

    public string? NameEn { get; set; }

    public int? PrepTimeMinutes { get; set; }

    public string TaxCategory { get; set; } = null!;

    public List<string>? ChannelVisibility { get; set; }

    public string CateringPricing { get; set; } = null!;

    public string? CateringPricingModel { get; set; }

    public string MenuType { get; set; } = null!;

    public List<string>? Allergens { get; set; }

    public string? BeverageType { get; set; }

    public List<string>? DietaryFlags { get; set; }

    public string? ItemCategory { get; set; }

    public string? VendorId { get; set; }

    public List<string>? CateringAllergens { get; set; }

    public virtual MenuCategory Category { get; set; } = null!;

    public virtual ICollection<FoodCostRecipe> FoodCostRecipes { get; set; } = new List<FoodCostRecipe>();

    public virtual ICollection<MarketplaceMenuMapping> MarketplaceMenuMappings { get; set; } = new List<MarketplaceMenuMapping>();

    public virtual ICollection<MenuItemModifierGroup> MenuItemModifierGroups { get; set; } = new List<MenuItemModifierGroup>();

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    public virtual ICollection<RecipeIngredient> RecipeIngredients { get; set; } = new List<RecipeIngredient>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
