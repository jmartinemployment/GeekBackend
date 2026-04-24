using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class RecurringReservation
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? CustomerId { get; set; }

    public string CustomerName { get; set; } = null!;

    public string? CustomerPhone { get; set; }

    public int DayOfWeek { get; set; }

    public string Time { get; set; } = null!;

    public int PartySize { get; set; }

    public bool IsActive { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
