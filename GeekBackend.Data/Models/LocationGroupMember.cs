using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class LocationGroupMember
{
    public string Id { get; set; } = null!;

    public string LocationGroupId { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual LocationGroup LocationGroup { get; set; } = null!;

    public virtual Restaurant Restaurant { get; set; } = null!;
}
