using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CycleCount
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public DateOnly Date { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? CompletedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<CycleCountItem> CycleCountItems { get; set; } = new List<CycleCountItem>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
