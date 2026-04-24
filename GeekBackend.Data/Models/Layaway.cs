using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Layaway
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? CustomerId { get; set; }

    public string? CustomerName { get; set; }

    public string Items { get; set; } = null!;

    public decimal TotalAmount { get; set; }

    public decimal DepositPaid { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
