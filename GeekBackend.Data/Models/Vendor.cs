using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Vendor
{
    public string Id { get; set; } = null!;

    public string RestaurantId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? ContactName { get; set; }

    public string? ContactEmail { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? ApiPortalUrl { get; set; }

    public bool IsIntegrated { get; set; }

    public int? LeadTimeDays { get; set; }

    public string? PaymentTerms { get; set; }

    public string? Website { get; set; }

    public virtual ICollection<PurchaseInvoice> PurchaseInvoices { get; set; } = new List<PurchaseInvoice>();

    public virtual Restaurant Restaurant { get; set; } = null!;
}
