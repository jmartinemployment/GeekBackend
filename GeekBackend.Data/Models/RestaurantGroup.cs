using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RestaurantGroup
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string? Description { get; set; }

    public string? Logo { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<LocationGroup> LocationGroups { get; set; } = new List<LocationGroup>();

    public virtual ICollection<MenuSyncLog> MenuSyncLogs { get; set; } = new List<MenuSyncLog>();

    public virtual ICollection<Restaurant> Restaurants { get; set; } = new List<Restaurant>();

    public virtual ICollection<TeamMember> TeamMembers { get; set; } = new List<TeamMember>();
}
