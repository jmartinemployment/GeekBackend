using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CustomerFeedback
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? CustomerId { get; set; }

    public string? OrderId { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }

    public string Source { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
