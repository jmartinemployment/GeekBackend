using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Reservation
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? CustomerId { get; set; }

    public string CustomerName { get; set; } = null!;

    public string CustomerPhone { get; set; } = null!;

    public string? CustomerEmail { get; set; }

    public int PartySize { get; set; }

    public DateTime ReservationTime { get; set; }

    public string? TableNumber { get; set; }

    public string Status { get; set; } = null!;

    public string? SpecialRequests { get; set; }

    public bool ConfirmationSent { get; set; }

    public bool ReminderSent { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual Restaurant Restaurant { get; set; } = null!;
}
