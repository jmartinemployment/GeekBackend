using System;
using System.Collections.Generic;

namespace GeekBackend.Data.Models;

public partial class Site
{
    public string Id { get; set; } = null!;

    public string UserId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Domain { get; set; } = null!;

    public string CmsType { get; set; } = null!;

    public string? CmsApiUrl { get; set; }

    public string? CmsApiKey { get; set; }

    public string? CmsSiteId { get; set; }

    public string Language { get; set; } = null!;

    public string PostFrequency { get; set; } = null!;

    public bool AutoPublish { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Analytic> Analytics { get; set; } = new List<Analytic>();

    public virtual ICollection<Article> Articles { get; set; } = new List<Article>();

    public virtual ICollection<Audience> Audiences { get; set; } = new List<Audience>();

    public virtual ICollection<BrandVoice> BrandVoices { get; set; } = new List<BrandVoice>();

    public virtual ICollection<Campaign> Campaigns { get; set; } = new List<Campaign>();

    public virtual ICollection<ContentCalendar> ContentCalendars { get; set; } = new List<ContentCalendar>();

    public virtual ICollection<Keyword> Keywords { get; set; } = new List<Keyword>();

    public virtual ICollection<KnowledgeBase> KnowledgeBases { get; set; } = new List<KnowledgeBase>();

    public virtual User2 User { get; set; } = null!;
}
