using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class ComplianceAlert
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? TeamMemberId { get; set; }

    public string Type { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public string Message { get; set; } = null!;

    public DateOnly Date { get; set; }

    public bool IsResolved { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
