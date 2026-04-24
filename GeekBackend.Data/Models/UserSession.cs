using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class UserSession
{
    public string Id { get; set; } = null!;

    public string UserId { get; set; } = null!;

    public string Token { get; set; } = null!;

    public string? DeviceInfo { get; set; }

    public bool IsActive { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime LastActivityAt { get; set; }

    public virtual TeamMember User { get; set; } = null!;
}
