using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class AuditLog1
{
    public int Id { get; set; }

    public string Action { get; set; } = null!;

    public string? AdminId { get; set; }

    public string? TargetId { get; set; }

    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }
}
