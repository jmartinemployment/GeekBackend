using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class InvoiceLineItem
{
    public string Id { get; set; } = null!;

    public string InvoiceId { get; set; } = null!;

    public string Description { get; set; } = null!;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Total { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;
}
