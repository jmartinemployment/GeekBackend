using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CampaignPerformance
{
    public string Id { get; set; } = null!;

    public string CampaignId { get; set; } = null!;

    public int Sent { get; set; }

    public int Delivered { get; set; }

    public int Opened { get; set; }

    public int Clicked { get; set; }

    public int Converted { get; set; }

    public decimal Revenue { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Campaign1 Campaign { get; set; } = null!;
}
