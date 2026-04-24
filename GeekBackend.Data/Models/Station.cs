using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Station
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public bool IsExpo { get; set; }

    public string? Color { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<StationCategoryMapping> StationCategoryMappings { get; set; } = new List<StationCategoryMapping>();
}
