using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RestaurantTable
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string TableNumber { get; set; } = null!;

    public string? TableName { get; set; }

    public int Capacity { get; set; }

    public string? Section { get; set; }

    public string Status { get; set; } = null!;

    public int? PosX { get; set; }

    public int? PosY { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? ServerName { get; set; }

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
