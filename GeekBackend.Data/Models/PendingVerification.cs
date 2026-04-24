using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PendingVerification
{
    public string Id { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string OtpHash { get; set; } = null!;

    public int Attempts { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
