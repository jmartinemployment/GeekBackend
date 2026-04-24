using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class OrderStatusHistory
{
    public string Id { get; set; } = null!;

    public string OrderId { get; set; } = null!;

    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = null!;

    public string? ChangedBy { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Order Order { get; set; } = null!;
}
