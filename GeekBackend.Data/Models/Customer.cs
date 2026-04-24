using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Customer
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public int TotalOrders { get; set; }

    public decimal TotalSpent { get; set; }

    public decimal? AverageOrderValue { get; set; }

    public DateTime? LastOrderDate { get; set; }

    public int LoyaltyPoints { get; set; }

    public List<string>? Tags { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string LoyaltyTier { get; set; } = null!;

    public int TotalPointsEarned { get; set; }

    public int TotalPointsRedeemed { get; set; }

    public virtual ICollection<LoyaltyTransaction> LoyaltyTransactions { get; set; } = new List<LoyaltyTransaction>();

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
