using GeekAPI.Controllers.Workflow.Contracts;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.Export;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Infrastructure.InMemory;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GeekAPI.Controllers.Workflow;

[ApiController]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectStore _projectStore;
    private readonly CompanyProfileOptions _companyProfile;

    public ProjectsController(IProjectStore projectStore, IOptions<CompanyProfileOptions> companyProfile)
    {
        _projectStore = projectStore;
        _companyProfile = companyProfile.Value;
    }

    [HttpPost]
    public async Task<ActionResult<ProjectSummaryResponse>> Create([FromBody] CreateProjectRequest request, CancellationToken cancellationToken)
    {
        if (request.ClientId == Guid.Empty)
        {
            return BadRequest("ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ProjectUrl) || !Uri.IsWellFormedUriString(request.ProjectUrl, UriKind.Absolute))
        {
            return BadRequest("ProjectUrl must be a valid absolute URL.");
        }

        if (!Departments.IsValid(request.Department))
        {
            return BadRequest($"Department must be one of: {string.Join(", ", Departments.Slugs)}.");
        }

        var existing = (await _projectStore.ListAsync(
            p => p.Name == request.Name && p.TargetKeyword == request.TargetKeyword && p.ProjectUrl == request.ProjectUrl,
            cancellationToken)).FirstOrDefault();
        if (existing is not null)
        {
            return Ok(ToSummary(existing));
        }

        var project = new Project
        {
            ClientId = request.ClientId,
            Name = request.Name,
            ProjectUrl = request.ProjectUrl,
            TargetKeyword = request.TargetKeyword,
            Department = request.Department,
            PreferredProvider = request.PreferredProvider,
            UseExactKeywordAsTitle = request.UseExactKeywordAsTitle,
            SiteAnalysisId = request.SiteAnalysisId is Guid sid && sid != Guid.Empty ? sid : null,
        };

        await _projectStore.AddAsync(project, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = project.Id }, ToSummary(project));
    }

    private static readonly TimeSpan StaleProjectMaxAge = TimeSpan.FromHours(24);

    [HttpGet]
    public async Task<ActionResult<List<ProjectSummaryResponse>>> GetRecent(CancellationToken cancellationToken)
    {
        await _projectStore.PurgeStaleAsync(StaleProjectMaxAge, cancellationToken);
        var projects = await _projectStore.GetRecentAsync(cancellationToken: cancellationToken);
        return Ok(projects.Select(ToSummary).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDetailResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var project = await _projectStore.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        return Ok(ToDetail(project));
    }

    [HttpPut("{id:guid}/notes")]
    public async Task<ActionResult<ProjectDetailResponse>> UpdateNotes(
        Guid id, [FromBody] UpdateProjectNotesRequest request, CancellationToken cancellationToken)
    {
        var project = await _projectStore.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        project.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await _projectStore.SaveAsync(project, cancellationToken);

        return Ok(ToDetail(project));
    }

    [HttpPut("{id:guid}/hierarchy-context")]
    public async Task<ActionResult<ProjectDetailResponse>> UpdateHierarchyContext(
        Guid id, [FromBody] UpdateHierarchyContextRequest request, CancellationToken cancellationToken)
    {
        var project = await _projectStore.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (request.SiteAnalysisId is Guid sid && sid != Guid.Empty)
        {
            project.SiteAnalysisId = sid;
        }

        var children = (request.HierarchyChildHeadings ?? [])
            .Select(h => h?.Trim())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var toolNames = (request.HierarchyToolNames ?? [])
            .Select(t => t?.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var toolUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.HierarchyToolUrls is not null)
        {
            foreach (var kvp in request.HierarchyToolUrls)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                {
                    toolUrls[kvp.Key.Trim()] = kvp.Value.Trim();
                }
            }
        }

        var path = string.IsNullOrWhiteSpace(request.HierarchyPath) ? null : request.HierarchyPath.Trim();
        project.HierarchyPath = path;
        project.HierarchyChildHeadings = children;
        project.HierarchyToolNames = toolNames;
        project.HierarchyToolUrls = toolUrls;
        project.HierarchySourcePageUrl = string.IsNullOrWhiteSpace(request.HierarchySourcePageUrl)
            ? null
            : request.HierarchySourcePageUrl.Trim();
        project.AllowOutsideSiteScope = request.AllowOutsideSiteScope;

        var hasHierarchy = !string.IsNullOrWhiteSpace(path) || children.Count > 0 || toolNames.Count > 0;
        if (hasHierarchy || project.AllowOutsideSiteScope)
        {
            project.Status = ProjectStatus.ReadyForGeneration;
        }

        project.UpdatedAtUtc = DateTime.UtcNow;
        await _projectStore.SaveAsync(project, cancellationToken);

        return Ok(ToDetail(project));
    }

    [HttpPut("{id:guid}/serp-context")]
    public async Task<ActionResult<ProjectDetailResponse>> UpdateSerpContext(
        Guid id, [FromBody] UpdateSerpContextRequest request, CancellationToken cancellationToken)
    {
        var project = await _projectStore.GetAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        static string? NormalizeMultiline(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var lines = value
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        project.SerpTitles = NormalizeMultiline(request.SerpTitles);
        project.SerpUrls = NormalizeMultiline(request.SerpUrls);
        project.SerpPaaQuestions = NormalizeMultiline(request.SerpPaaQuestions);
        project.SerpRelatedSearches = NormalizeMultiline(request.SerpRelatedSearches);
        project.UpdatedAtUtc = DateTime.UtcNow;
        await _projectStore.SaveAsync(project, cancellationToken);

        return Ok(ToDetail(project));
    }

    [HttpPut("{id:guid}/brief")]
    public async Task<ActionResult<ProjectDetailResponse>> UpdateBrief(
        Guid id, [FromBody] UpdateProjectBriefRequest request, CancellationToken cancellationToken)
    {
        var project = await _projectStore.GetAsync(id, cancellationToken);
        if (project is null) return NotFound();
        if (request.CreateId == Guid.Empty) return BadRequest("CreateId is required.");

        project.LinkedCreateId = request.CreateId;
        project.BriefJson = string.IsNullOrWhiteSpace(request.BriefJson) ? null : request.BriefJson;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await _projectStore.SaveAsync(project, cancellationToken);
        return Ok(ToDetail(project));
    }

    private ProjectDetailResponse ToDetail(Project project)
    {
        var crawl = project.CrawledSite is null ? null : new CrawlSummaryResponse(
            project.CrawledSite.SiteName, project.CrawledSite.PagesCrawled,
            project.CrawledSite.DetectedTone, project.CrawledSite.DetectedFocus,
            project.CrawledSite.Headings.Count, project.CrawledSite.Paragraphs.Count, project.CrawledSite.JsonLdBlocks.Count);

        var keywordSources = project.KeywordSources.Select(k => new KeywordSourceResponse(
            k.Id, k.Category, k.OriginalFileName, k.ExtractedTitle,
            k.ExtractedHeadings.Count, k.ExtractedParagraphs.Count, k.ExtractedQuestions.Count,
            k.ExtractedToolResearchJson)).ToList();

        var generatedContent = project.GeneratedContents.Select(g => new GeneratedContentResponse(
            g.Id, g.ContentType, g.Title, g.Slug, g.MetaDescription, g.Keywords, g.WordCount,
            g.Body is null ? string.Empty : SectionHtmlRenderer.RenderFragment(g.Body),
            g.JsonLdSchema, g.RelatedArticleUrl, g.CreatedAtUtc, g.NoResearchWarning, g.Gaps)).ToList();

        var contentSet = project.GeneratedContents.Count == 0
            ? null
            : GeneratedContentSetAssembler.Assemble(
                project, project.Department, _companyProfile.ArticleBaseUrl, _companyProfile.BlogBaseUrl, _companyProfile.ToolBaseUrl);

        return new ProjectDetailResponse(
            project.Id, project.ClientId, project.Name, project.ProjectUrl, project.TargetKeyword, project.Department, project.Status,
            project.PreferredProvider, project.UseExactKeywordAsTitle, crawl, keywordSources, generatedContent, contentSet, project.Notes,
            project.ContentApprovedAtUtc,
            project.SiteAnalysisId,
            project.HierarchyPath,
            project.HierarchyChildHeadings,
            project.HierarchySourcePageUrl,
            project.AllowOutsideSiteScope,
            project.SerpTitles,
            project.SerpUrls,
            project.SerpPaaQuestions,
            project.SerpRelatedSearches,
            project.HierarchyToolNames,
            project.HierarchyToolUrls,
            project.LinkedCreateId);
    }

    private static ProjectSummaryResponse ToSummary(Project project) => new(
        project.Id, project.ClientId, project.Name, project.ProjectUrl, project.TargetKeyword, project.Department,
        project.Status, project.PreferredProvider, project.UseExactKeywordAsTitle, project.CreatedAtUtc,
        project.SiteAnalysisId);
}
