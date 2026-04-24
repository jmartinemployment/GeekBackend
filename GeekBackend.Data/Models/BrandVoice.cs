using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class BrandVoice
{
    public string Id { get; set; } = null!;

    public string SiteId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public List<string>? ToneWords { get; set; }

    public string? Examples { get; set; }

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Site Site { get; set; } = null!;
}
