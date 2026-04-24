using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RetailCategory
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int SortOrder { get; set; }

    public string? ParentId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<RetailCategory> InverseParent { get; set; } = new List<RetailCategory>();

    public virtual RetailCategory? Parent { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual ICollection<RetailItem> RetailItems { get; set; } = new List<RetailItem>();
}
