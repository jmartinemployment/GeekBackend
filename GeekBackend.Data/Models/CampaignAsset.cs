using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class CampaignAsset
{
    public string Id { get; set; } = null!;

    public string CampaignId { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? Platform { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Campaign Campaign { get; set; } = null!;
}
