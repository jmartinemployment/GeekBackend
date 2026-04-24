using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Device1
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string DeviceType { get; set; } = null!;

    public string DeviceName { get; set; } = null!;

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? DeviceCode { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? HardwareInfo { get; set; }

    public string? LocationId { get; set; }

    public string? ModeId { get; set; }

    public DateTime? PairedAt { get; set; }

    public string? PosMode { get; set; }

    public string Status { get; set; } = null!;

    public string? TeamMemberId { get; set; }

    public DateTime? MfaExpiresAt { get; set; }

    public DateTime? MfaVerifiedAt { get; set; }

    public virtual DeviceMode? Mode { get; set; }

    public virtual ICollection<PeripheralDevice> PeripheralDevices { get; set; } = new List<PeripheralDevice>();

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual TeamMember? TeamMember { get; set; }
}
