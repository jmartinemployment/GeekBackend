using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.HttpClients;

namespace GeekAPI.Services.ContentCreatorV2.TaskAgents;

/// <summary>Renders Studio instruction templates and parses studio ownership facets.</summary>
internal static partial class GccV2StudioTemplateRenderer
{
    public const string Category = "studio";
    public const string ArtifactType = "customAgentOutput.v1";
    public const string Methodology = "instruction-template";
    public const string Engine = "studio";
    public const string Mode = "instruction-template";

    [GeneratedRegex(@"\{\{[^}]+\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public static StudioRenderResult Render(
        string instructionsTemplate,
        string agentName,
        string outcome,
        JsonElement inputs)
    {
        var rendered = instructionsTemplate
            .Replace("{{outcome}}", outcome ?? "", StringComparison.Ordinal)
            .Replace("{{agent.name}}", agentName ?? "", StringComparison.Ordinal);

        if (inputs.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in inputs.EnumerateObject())
            {
                var token = "{{inputs." + property.Name + "}}";
                rendered = rendered.Replace(token, FormatInputValue(property.Value), StringComparison.Ordinal);
            }
        }

        var missingTokens = FindUnresolvedTokens(rendered);
        return new StudioRenderResult(rendered, missingTokens);
    }

    public static IReadOnlyList<string> FindUnresolvedTokens(string rendered) =>
        TokenRegex().Matches(rendered).Select(x => x.Value).Distinct(StringComparer.Ordinal).ToList();

    public static StudioFacets? TryParseFacets(string? facetsJson)
    {
        if (string.IsNullOrWhiteSpace(facetsJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(facetsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var category = root.TryGetProperty("category", out var categoryEl)
                && categoryEl.ValueKind == JsonValueKind.String
                ? categoryEl.GetString()
                : null;
            var ownerUserId = root.TryGetProperty("ownerUserId", out var ownerEl)
                && ownerEl.ValueKind == JsonValueKind.String
                ? ownerEl.GetString()
                : null;
            var visibility = root.TryGetProperty("visibility", out var visibilityEl)
                && visibilityEl.ValueKind == JsonValueKind.String
                ? visibilityEl.GetString()
                : null;
            var methodology = root.TryGetProperty("methodology", out var methodologyEl)
                && methodologyEl.ValueKind == JsonValueKind.String
                ? methodologyEl.GetString()
                : null;
            return new StudioFacets(category, ownerUserId, visibility, methodology);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsStudioVersion(GccV2TaskAgentVersionDto version) =>
        TryParseFacets(version.FacetsJson) is { Category: Category };

    public static bool IsOwnedBy(GccV2TaskAgentVersionDto version, string ownerUserId) =>
        TryParseFacets(version.FacetsJson) is { Category: Category, OwnerUserId: { } owner }
        && string.Equals(owner, ownerUserId, StringComparison.OrdinalIgnoreCase);

    public static bool IsPrivateStudioNotOwned(GccV2TaskAgentVersionDto version, string ownerUserId)
    {
        var facets = TryParseFacets(version.FacetsJson);
        return facets is { Category: Category, Visibility: "private" }
            && !string.Equals(facets.OwnerUserId, ownerUserId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAdminSharedPublished(GccV2TaskAgentVersionDto version) =>
        version.State == "published"
        && TryParseFacets(version.FacetsJson) is { Category: Category, Visibility: "admin_shared" };

    public static bool CanAccessPublishedStudio(
        GccV2TaskAgentVersionDto version, string ownerUserId)
    {
        if (!IsStudioVersion(version)) return true;
        if (IsOwnedBy(version, ownerUserId)) return true;
        return version.State == "published"
            && TryParseFacets(version.FacetsJson) is { Visibility: "admin_shared" };
    }

    public static bool IsVisibleInCatalog(GccV2TaskAgentVersionDto version, string ownerUserId)
    {
        if (!IsStudioVersion(version)) return true;
        if (IsOwnedBy(version, ownerUserId)) return true;
        return version.State == "published"
            && TryParseFacets(version.FacetsJson) is { Visibility: "admin_shared" };
    }

    private static string FormatInputValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? ""
                : item.GetRawText())),
        JsonValueKind.Null => "",
        _ => value.GetRawText(),
    };

    internal sealed record StudioRenderResult(
        string RenderedInstructions, IReadOnlyList<string> MissingTokens);

    internal sealed record StudioFacets(
        string? Category, string? OwnerUserId, string? Visibility, string? Methodology);
}
