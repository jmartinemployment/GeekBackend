using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;

namespace GeekAPI.Services.Workflow.Infrastructure.Serialization;

/// <summary>
/// Serializes Project to/from JSON for durable persistence. The Project.Client navigation
/// is nulled before serialization to avoid duplication; Client is rehydrated from the
/// client cache on load.
/// </summary>
public static class ProjectSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TolerantNullableLedeTypeConverter(), new StrictLedeTypeConverter(), new JsonStringEnumConverter() }
    };

    public static string Serialize(Project project)
    {
        // Snapshot excludes the Client navigation; it's rehydrated from the client cache on load.
        var snapshot = new ProjectSnapshot(
            SchemaVersion: 5,
            Id: project.Id,
            ClientId: project.ClientId,
            Name: project.Name,
            ProjectUrl: project.ProjectUrl,
            TargetKeyword: project.TargetKeyword,
            Department: project.Department,
            Status: project.Status,
            PreferredProvider: project.PreferredProvider,
            UseExactKeywordAsTitle: project.UseExactKeywordAsTitle,
            Notes: project.Notes,
            SiteAnalysisId: project.SiteAnalysisId,
            SiteAnalysisProfileId: project.SiteAnalysisProfileId,
            HierarchyPath: project.HierarchyPath,
            HierarchyChildHeadings: project.HierarchyChildHeadings,
            HierarchyToolsByHeading: project.HierarchyToolsByHeading,
            HierarchyAssignmentMarkdown: project.HierarchyAssignmentMarkdown,
            HierarchySourcePageUrl: project.HierarchySourcePageUrl,
            AllowOutsideSiteScope: project.AllowOutsideSiteScope,
            SerpTitles: project.SerpTitles,
            SerpUrls: project.SerpUrls,
            SerpPaaQuestions: project.SerpPaaQuestions,
            SerpRelatedSearches: project.SerpRelatedSearches,
            LinkedCreateId: project.LinkedCreateId,
            BriefJson: project.BriefJson,
            CreatedAtUtc: project.CreatedAtUtc,
            UpdatedAtUtc: project.UpdatedAtUtc,
            ContentApprovedAtUtc: project.ContentApprovedAtUtc,
            CrawledSite: project.CrawledSite,
            KeywordSources: project.KeywordSources,
            GeneratedContents: project.GeneratedContents);

        return JsonSerializer.Serialize(snapshot, Options);
    }

    public static Project Deserialize(string json, Client? client)
    {
        var snapshot = JsonSerializer.Deserialize<ProjectSnapshot>(json, Options)
            ?? throw new InvalidOperationException("Failed to deserialize project snapshot.");

        return new Project
        {
            Id = snapshot.Id,
            ClientId = snapshot.ClientId,
            Name = snapshot.Name,
            ProjectUrl = snapshot.ProjectUrl,
            TargetKeyword = snapshot.TargetKeyword,
            Department = snapshot.Department,
            Status = snapshot.Status,
            PreferredProvider = snapshot.PreferredProvider,
            UseExactKeywordAsTitle = snapshot.UseExactKeywordAsTitle,
            Notes = snapshot.Notes,
            SiteAnalysisId = snapshot.SiteAnalysisId,
            SiteAnalysisProfileId = snapshot.SiteAnalysisProfileId,
            HierarchyPath = snapshot.HierarchyPath,
            HierarchyChildHeadings = snapshot.HierarchyChildHeadings ?? new(),
            HierarchyToolsByHeading = snapshot.HierarchyToolsByHeading ?? new(),
            HierarchyAssignmentMarkdown = snapshot.HierarchyAssignmentMarkdown,
            HierarchySourcePageUrl = snapshot.HierarchySourcePageUrl,
            AllowOutsideSiteScope = snapshot.AllowOutsideSiteScope,
            SerpTitles = snapshot.SerpTitles,
            SerpUrls = snapshot.SerpUrls,
            SerpPaaQuestions = snapshot.SerpPaaQuestions,
            SerpRelatedSearches = snapshot.SerpRelatedSearches,
            LinkedCreateId = snapshot.LinkedCreateId,
            BriefJson = snapshot.BriefJson,
            CreatedAtUtc = snapshot.CreatedAtUtc,
            UpdatedAtUtc = snapshot.UpdatedAtUtc,
            ContentApprovedAtUtc = snapshot.ContentApprovedAtUtc,
            Client = client,
            CrawledSite = snapshot.CrawledSite,
            KeywordSources = snapshot.KeywordSources ?? new(),
            GeneratedContents = snapshot.GeneratedContents ?? new()
        };
    }

    private sealed record ProjectSnapshot(
        int SchemaVersion,
        Guid Id,
        Guid ClientId,
        string Name,
        string ProjectUrl,
        string TargetKeyword,
        string Department,
        ProjectStatus Status,
        LlmProviderType PreferredProvider,
        bool UseExactKeywordAsTitle,
        string? Notes,
        Guid? SiteAnalysisId,
        Guid? SiteAnalysisProfileId,
        string? HierarchyPath,
        List<string>? HierarchyChildHeadings,
        List<ToolsByHeading>? HierarchyToolsByHeading,
        string? HierarchyAssignmentMarkdown,
        string? HierarchySourcePageUrl,
        bool AllowOutsideSiteScope,
        string? SerpTitles,
        string? SerpUrls,
        string? SerpPaaQuestions,
        string? SerpRelatedSearches,
        Guid? LinkedCreateId,
        string? BriefJson,
        DateTime CreatedAtUtc,
        DateTime? UpdatedAtUtc,
        DateTime? ContentApprovedAtUtc,
        CrawledSite? CrawledSite,
        List<KeywordSource> KeywordSources,
        List<GeneratedContent> GeneratedContents);
}
