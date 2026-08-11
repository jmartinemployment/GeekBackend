using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.Domain.Enums;

namespace GeekAPI.Services.Workflow.Domain.Entities;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProjectUrl { get; set; } = string.Empty;
    public string TargetKeyword { get; set; } = string.Empty;

    /// <summary>Department/category slug (e.g. "accounting") — determines the published URL segment: /use-cases/{Department}/{slug}.</summary>
    public string Department { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public LlmProviderType PreferredProvider { get; set; } = LlmProviderType.LmStudio;

    /// <summary>When true, skip LLM title generation for the pillar article and use TargetKeyword verbatim as the title.</summary>
    public bool UseExactKeywordAsTitle { get; set; }

    /// <summary>Optional comma-separated desired headings that must appear in the pillar article outline.</summary>
    public string? Notes { get; set; }

    /// <summary>Site Analyzer profile/analysis id from Workflow handoff — grounds hierarchy match.</summary>
    public Guid? SiteAnalysisId { get; set; }

    /// <summary>Matched SA heading breadcrumb (e.g. "Services › HVAC › Installation").</summary>
    public string? HierarchyPath { get; set; }

    /// <summary>Child heading texts under the matched SA node — pillar/blog must emit these as child headings.</summary>
    public List<string> HierarchyChildHeadings { get; set; } = new();

    /// <summary>
    /// Tool names parsed from the matched SA node's "Top … Tools: a, b, c" paragraph.
    /// Empty when the node has no such paragraph — never LLM-invented.
    /// </summary>
    public List<string> HierarchyToolNames { get; set; } = new();

    /// <summary>Source page URL for the matched hierarchy node.</summary>
    public string? HierarchySourcePageUrl { get; set; }

    /// <summary>
    /// Operator ack that the target keyword is outside site hierarchy scope.
    /// Required to generate when HierarchyPath is null/empty (no crawl/tone/focus filler).
    /// </summary>
    public bool AllowOutsideSiteScope { get; set; }

    /// <summary>Curated SERP organic titles (one per line) — from saved Google results + operator confirm.</summary>
    public string? SerpTitles { get; set; }

    /// <summary>Curated SERP organic URLs (one per line), paired with SerpTitles.</summary>
    public string? SerpUrls { get; set; }

    /// <summary>Operator-curated People Also Ask questions (one per line).</summary>
    public string? SerpPaaQuestions { get; set; }

    /// <summary>Curated related searches (one per line).</summary>
    public string? SerpRelatedSearches { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Content Creator: operator content-approval timestamp (gates Repurpose / Mix).</summary>
    public DateTime? ContentApprovedAtUtc { get; set; }

    /// <summary>Back-reference to the owning row; not serialized (ClientId is the durable FK) — a populated value here forms a JSON cycle through Client.Projects.</summary>
    [JsonIgnore]
    public Client? Client { get; set; }
    public CrawledSite? CrawledSite { get; set; }
    public List<KeywordSource> KeywordSources { get; set; } = new();
    public List<GeneratedContent> GeneratedContents { get; set; } = new();
}
