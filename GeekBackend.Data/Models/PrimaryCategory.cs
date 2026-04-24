using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PrimaryCategory
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? NameEn { get; set; }

    public string? Icon { get; set; }

    public int DisplayOrder { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<MenuCategory> MenuCategories { get; set; } = new List<MenuCategory>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
