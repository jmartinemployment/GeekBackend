using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PasswordResetToken
{
    public string Id { get; set; } = null!;

    public string TeamMemberId { get; set; } = null!;

    public string Token { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual TeamMember TeamMember { get; set; } = null!;
}
