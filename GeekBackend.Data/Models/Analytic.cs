using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Analytic
{
    public string Id { get; set; } = null!;

    public string SiteId { get; set; } = null!;

    public DateTime Date { get; set; }

    public int Clicks { get; set; }

    public int Impressions { get; set; }

    public double Position { get; set; }

    public double Ctr { get; set; }

    public string? Keyword { get; set; }

    public string? Page { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Site Site { get; set; } = null!;
}
