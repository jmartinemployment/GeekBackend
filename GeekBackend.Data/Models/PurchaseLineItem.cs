using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PurchaseLineItem
{
    public string Id { get; set; } = null!;

    public string InvoiceId { get; set; } = null!;

    public string IngredientName { get; set; } = null!;

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = null!;

    public decimal UnitCost { get; set; }

    public decimal TotalCost { get; set; }

    public string? NormalizedIngredient { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual PurchaseInvoice Invoice { get; set; } = null!;
}
