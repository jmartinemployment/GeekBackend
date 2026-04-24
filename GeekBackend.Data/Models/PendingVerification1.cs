using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PendingVerification1
{
    public string Id { get; set; } = null!;

    public string EmailHash { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string OtpHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
