using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CheckDiscount
{
    public string Id { get; set; } = null!;

    public string CheckId { get; set; } = null!;

    public string OrderId { get; set; } = null!;

    public string Type { get; set; } = null!;

    public decimal Value { get; set; }

    public string Reason { get; set; } = null!;

    public string AppliedBy { get; set; } = null!;

    public string? ApprovedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual OrderCheck Check { get; set; } = null!;
}
