using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Keyword
{
    public string Id { get; set; } = null!;

    public string SiteId { get; set; } = null!;

    public string Keyword1 { get; set; } = null!;

    public string? SearchVolume { get; set; }

    public int? Difficulty { get; set; }

    public string? Intent { get; set; }

    public string Status { get; set; } = null!;

    public string? TopicCluster { get; set; }

    public bool LongTail { get; set; }

    public string? SuggestedTitle { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Site Site { get; set; } = null!;
}
