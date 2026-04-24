using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Campaign
{
    public string Id { get; set; } = null!;

    public string SiteId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Brief { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<CampaignAsset> CampaignAssets { get; set; } = new List<CampaignAsset>();

    public virtual Site Site { get; set; } = null!;
}
