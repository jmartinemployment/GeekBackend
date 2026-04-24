using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class UserRestaurantAccess
{
    public string Id { get; set; } = null!;

    public string UserId { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Role { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual TeamMember User { get; set; } = null!;
}
