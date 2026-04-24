using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CateringPackageTemplate
{
    public string Id { get; set; } = null!;

    public string MerchantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Tier { get; set; } = null!;

    public string PricingModel { get; set; } = null!;

    public int PricePerUnitCents { get; set; }

    public int MinimumHeadcount { get; set; }

    public string? Description { get; set; }

    public string MenuItemIds { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
