using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Device
{
    public string Id { get; set; } = null!;

    public string UserId { get; set; } = null!;

    public string DeviceId { get; set; } = null!;

    public string? UserAgent { get; set; }

    public string? IpAddress { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? MfaVerifiedAt { get; set; }

    public DateTime? MfaExpiresAt { get; set; }

    public DateTime LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User1 User { get; set; } = null!;
}
