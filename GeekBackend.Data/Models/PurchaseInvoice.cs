using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class PurchaseInvoice
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string VendorId { get; set; } = null!;

    public string InvoiceNumber { get; set; } = null!;

    public DateTime InvoiceDate { get; set; }

    public decimal TotalAmount { get; set; }

    public string Status { get; set; } = null!;

    public string? ImageUrl { get; set; }

    public DateTime? OcrProcessedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<PurchaseLineItem> PurchaseLineItems { get; set; } = new List<PurchaseLineItem>();

    public virtual Restaurant Restaurant { get; set; } = null!;

    public virtual Vendor Vendor { get; set; } = null!;
}
