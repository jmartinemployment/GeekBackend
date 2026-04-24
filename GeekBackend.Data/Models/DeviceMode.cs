using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class DeviceMode
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string DeviceType { get; set; } = null!;

    public bool IsDefault { get; set; }

    public string Settings { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Device1> Device1s { get; set; } = new List<Device1>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
