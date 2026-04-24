using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PeripheralDevice
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string ParentDeviceId { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string ConnectionType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Device1 ParentDevice { get; set; } = null!;

    public virtual Restaurant Restaurant { get; set; } = null!;
}
