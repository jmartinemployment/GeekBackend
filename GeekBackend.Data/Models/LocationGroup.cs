using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class LocationGroup
{
    public string Id { get; set; } = null!;

    public string RestaurantGroupId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<LocationGroupMember> LocationGroupMembers { get; set; } = new List<LocationGroupMember>();

    public virtual RestaurantGroup RestaurantGroup { get; set; } = null!;
}
