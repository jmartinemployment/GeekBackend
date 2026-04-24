using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class MfaSecret
{
    public string Id { get; set; } = null!;

    public string TeamMemberId { get; set; } = null!;

    public string? Secret { get; set; }

    public bool Verified { get; set; }

    public List<string>? BackupCodes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? EmailOtpExpiry { get; set; }

    public string? EmailOtpHash { get; set; }

    public string MfaType { get; set; } = null!;

    public virtual TeamMember TeamMember { get; set; } = null!;
}
