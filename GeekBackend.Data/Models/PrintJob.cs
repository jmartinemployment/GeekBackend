using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PrintJob
{
    public string Id { get; set; } = null!;

    public string OrderId { get; set; } = null!;

    public string PrinterId { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public string JobData { get; set; } = null!;

    public virtual Order Order { get; set; } = null!;

    public virtual Printer Printer { get; set; } = null!;
}
