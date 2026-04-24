using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RetailItem
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? CategoryId { get; set; }

    public string Name { get; set; } = null!;

    public string? Sku { get; set; }

    public string? Barcode { get; set; }

    public decimal Price { get; set; }

    public decimal? Cost { get; set; }

    public string? Description { get; set; }

    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; }

    public bool TrackStock { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual RetailCategory? Category { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual ICollection<RetailItemOptionSet> RetailItemOptionSets { get; set; } = new List<RetailItemOptionSet>();

    public virtual RetailStock? RetailStock { get; set; }
}
