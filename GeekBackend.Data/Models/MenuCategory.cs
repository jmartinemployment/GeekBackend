using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MenuCategory
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public string? Image { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? DescriptionEn { get; set; }

    public string? NameEn { get; set; }

    public string? PrimaryCategoryId { get; set; }

    public List<string>? ChannelVisibility { get; set; }

    public virtual ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();

    public virtual PrimaryCategory? PrimaryCategory { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual ICollection<StationCategoryMapping> StationCategoryMappings { get; set; } = new List<StationCategoryMapping>();
}
