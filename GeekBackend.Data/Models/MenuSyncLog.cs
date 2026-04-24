using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MenuSyncLog
{
    public string Id { get; set; } = null!;

    public string RestaurantGroupId { get; set; } = null!;

    public string SourceRestaurantId { get; set; } = null!;

    public string TargetRestaurantIds { get; set; } = null!;

    public int ItemsAdded { get; set; }

    public int ItemsUpdated { get; set; }

    public int ItemsSkipped { get; set; }

    public int Conflicts { get; set; }

    public string? SyncedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual RestaurantGroup RestaurantGroup { get; set; } = null!;
}
