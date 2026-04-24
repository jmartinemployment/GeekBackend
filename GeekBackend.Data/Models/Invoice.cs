using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Invoice
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string InvoiceNumber { get; set; } = null!;

    public string? CustomerId { get; set; }

    public string CustomerName { get; set; } = null!;

    public string CustomerEmail { get; set; } = null!;

    public string? HouseAccountId { get; set; }

    public string Status { get; set; } = null!;

    public decimal Subtotal { get; set; }

    public decimal Tax { get; set; }

    public decimal Total { get; set; }

    public decimal PaidAmount { get; set; }

    public DateTime DueDate { get; set; }

    public string? Notes { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual HouseAccount? HouseAccount { get; set; }

    public virtual ICollection<InvoiceLineItem> InvoiceLineItems { get; set; } = new List<InvoiceLineItem>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
