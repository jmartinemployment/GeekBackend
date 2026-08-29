using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

public static class GccV2ToolSlugHelper
{
    public const string DefaultDepartment = "marketing";

    public static string SlugifyToolName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "tool" : SlugHelper.Slugify(name.Trim());

    public static string SlugifyKeyword(string? keyword) =>
        string.IsNullOrWhiteSpace(keyword) ? "tool-overview" : SlugHelper.Slugify(keyword.Trim());

    public static string OnSiteHref(string slug, string department = DefaultDepartment) =>
        $"/tools/{department}/{slug}";
}
