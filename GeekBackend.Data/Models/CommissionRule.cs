using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CommissionRule
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Type { get; set; } = null!;

    public decimal Value { get; set; }

    public string AppliesTo { get; set; } = null!;

    public string JobTitles { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
