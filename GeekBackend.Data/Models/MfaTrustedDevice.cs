using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MfaTrustedDevice
{
    public string Id { get; set; } = null!;

    public string TeamMemberId { get; set; } = null!;

    public string UaFingerprint { get; set; } = null!;

    public string? DeviceInfo { get; set; }

    public DateTime TrustedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public virtual TeamMember TeamMember { get; set; } = null!;
}
