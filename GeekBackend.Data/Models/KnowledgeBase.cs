using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class KnowledgeBase
{
    public string Id { get; set; } = null!;

    public string SiteId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Type { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? SourceUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Site Site { get; set; } = null!;
}
