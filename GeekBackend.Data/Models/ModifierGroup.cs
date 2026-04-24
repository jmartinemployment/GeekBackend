using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class ModifierGroup
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? NameEn { get; set; }

    public string? Description { get; set; }

    public string? DescriptionEn { get; set; }

    public bool Required { get; set; }

    public bool MultiSelect { get; set; }

    public int MinSelections { get; set; }

    public int? MaxSelections { get; set; }

    public int DisplayOrder { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<MenuItemModifierGroup> MenuItemModifierGroups { get; set; } = new List<MenuItemModifierGroup>();

    public virtual ICollection<Modifier> Modifiers { get; set; } = new List<Modifier>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
