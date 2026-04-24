using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class HouseAccount
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string ContactName { get; set; } = null!;

    public string ContactEmail { get; set; } = null!;

    public decimal CreditLimit { get; set; }

    public decimal CurrentBalance { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
