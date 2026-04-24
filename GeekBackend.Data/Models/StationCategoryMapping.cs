using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class StationCategoryMapping
{
    public string Id { get; set; } = null!;

    public string StationId { get; set; } = null!;

    public string CategoryId { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual MenuCategory Category { get; set; } = null!;

    public virtual Station Station { get; set; } = null!;
}
