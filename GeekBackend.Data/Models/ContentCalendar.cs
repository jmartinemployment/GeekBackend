using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class ContentCalendar
{
    public string Id { get; set; } = null!;

    public string SiteId { get; set; } = null!;

    public string Keyword { get; set; } = null!;

    public DateTime ScheduledAt { get; set; }

    public string Status { get; set; } = null!;

    public string? ArticleId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Site Site { get; set; } = null!;
}
