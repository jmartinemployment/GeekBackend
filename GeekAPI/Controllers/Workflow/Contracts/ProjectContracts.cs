using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekAPI.Controllers.Workflow.Contracts;

public record CreateProjectRequest(
    Guid ClientId, string Name, string ProjectUrl, string TargetKeyword, string Department,
    LlmProviderType PreferredProvider, bool UseExactKeywordAsTitle = false,
    Guid? SiteAnalysisId = null,
    Guid? SiteAnalysisProfileId = null);

public record UpdateProjectNotesRequest(string? Notes);

public record UpdateHierarchyContextRequest(
    string? HierarchyPath,
    IReadOnlyList<string>? HierarchyChildHeadings,
    string? HierarchySourcePageUrl,
    bool AllowOutsideSiteScope,
    Guid? SiteAnalysisId = null,
    Guid? SiteAnalysisProfileId = null,
    IReadOnlyList<ToolsByHeading>? HierarchyToolsByHeading = null,
    string? HierarchyAssignmentMarkdown = null);

public record UpdateSerpContextRequest(
    string? SerpTitles,
    string? SerpUrls,
    string? SerpPaaQuestions,
    string? SerpRelatedSearches);

public record UpdateProjectBriefRequest(Guid CreateId, string? BriefJson);

public record ProjectSummaryResponse(
    Guid Id, Guid ClientId, string Name, string ProjectUrl, string TargetKeyword, string Department,
    ProjectStatus Status, LlmProviderType PreferredProvider, bool UseExactKeywordAsTitle, DateTime CreatedAtUtc,
    Guid? SiteAnalysisId = null,
    Guid? SiteAnalysisProfileId = null);

public record ProjectDetailResponse(
    Guid Id, Guid ClientId, string Name, string ProjectUrl, string TargetKeyword, string Department, ProjectStatus Status,
    LlmProviderType PreferredProvider, bool UseExactKeywordAsTitle, CrawlSummaryResponse? Crawl,
    List<KeywordSourceResponse> KeywordSources, List<GeneratedContentResponse> GeneratedContent,
    GeneratedContentSet? ContentSet, string? Notes, DateTime? ContentApprovedAtUtc = null,
    Guid? SiteAnalysisId = null,
    Guid? SiteAnalysisProfileId = null,
    string? HierarchyPath = null,
    IReadOnlyList<string>? HierarchyChildHeadings = null,
    string? HierarchySourcePageUrl = null,
    bool AllowOutsideSiteScope = false,
    string? SerpTitles = null,
    string? SerpUrls = null,
    string? SerpPaaQuestions = null,
    string? SerpRelatedSearches = null,
    IReadOnlyList<ToolsByHeading>? HierarchyToolsByHeading = null,
    Guid? LinkedCreateId = null,
    string? HierarchyAssignmentMarkdown = null);

public record CrawlSummaryResponse(
    string SiteName, int PagesCrawled, string DetectedTone, string DetectedFocus,
    int HeadingCount, int ParagraphCount, int JsonLdBlockCount);

public record KeywordSourceResponse(
    Guid Id, KeywordSourceCategory Category, string OriginalFileName,
    string? ExtractedTitle, int HeadingCount, int ParagraphCount, int QuestionCount,
    string? ExtractedToolResearchJson = null,
    string? ToolsMismatchWarning = null);

public record GeneratedContentResponse(
    Guid Id, GeneratedContentType ContentType, string Title, string Slug,
    string? MetaDescription, IReadOnlyList<string> Keywords, int WordCount,
    string BodyHtml, string? JsonLdSchema, string? RelatedArticleUrl, DateTime CreatedAtUtc,
    string? NoResearchWarning, IReadOnlyList<string> Gaps);
