using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RestaurantProviderProfile
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Provider { get; set; } = null!;

    public string? ConfigRefMap { get; set; }

    public int ProfileVersion { get; set; }

    public string ProfileState { get; set; } = null!;

    public string KeyBackend { get; set; } = null!;

    public string? KeyRef { get; set; }

    public string? WrappedDek { get; set; }

    public int DekVersion { get; set; }

    public string? AadHash { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? RotatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual ICollection<RestaurantProviderProfileEvent> RestaurantProviderProfileEvents { get; set; } = new List<RestaurantProviderProfileEvent>();
}
