using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class TaxJurisdiction
{
    public string Id { get; set; } = null!;

    public string ZipCode { get; set; } = null!;

    public string? City { get; set; }

    public string? County { get; set; }

    public string State { get; set; } = null!;

    public decimal TaxRate { get; set; }

    public string? Breakdown { get; set; }

    public string Source { get; set; } = null!;

    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
