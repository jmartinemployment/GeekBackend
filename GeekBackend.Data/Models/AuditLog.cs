using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class AuditLog
{
    public string Id { get; set; } = null!;

    public string? UserId { get; set; }

    public string Action { get; set; } = null!;

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }
}
