using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2StudioTemplateRendererTests
{
    [Fact]
    public void Render_replaces_outcome_agent_name_and_input_tokens()
    {
        using var inputs = JsonDocument.Parse("""{"topic":"AI search","tags":["a","b"]}""");
        var result = GccV2StudioTemplateRenderer.Render(
            "Outcome: {{outcome}}\nAgent: {{agent.name}}\nTopic: {{inputs.topic}}\nTags: {{inputs.tags}}",
            "Brand Agent",
            "Produce a brief",
            inputs.RootElement);

        Assert.Equal(
            "Outcome: Produce a brief\nAgent: Brand Agent\nTopic: AI search\nTags: a, b",
            result.RenderedInstructions);
        Assert.Empty(result.MissingTokens);
    }

    [Fact]
    public void Render_reports_unresolved_tokens()
    {
        using var inputs = JsonDocument.Parse("""{"topic":"x"}""");
        var result = GccV2StudioTemplateRenderer.Render(
            "Hello {{inputs.topic}} and {{inputs.missing}} plus {{unknown}}",
            "Name",
            "Outcome",
            inputs.RootElement);

        Assert.Contains("{{inputs.missing}}", result.MissingTokens);
        Assert.Contains("{{unknown}}", result.MissingTokens);
        Assert.DoesNotContain("{{inputs.topic}}", result.MissingTokens);
    }

    [Fact]
    public void TryParseFacets_reads_studio_ownership()
    {
        var facets = GccV2StudioTemplateRenderer.TryParseFacets(
            """{"category":"studio","ownerUserId":"11111111-1111-1111-1111-111111111111","visibility":"private","methodology":"instruction-template"}""");
        Assert.NotNull(facets);
        Assert.Equal("studio", facets!.Category);
        Assert.Equal("private", facets.Visibility);
        Assert.Equal("11111111-1111-1111-1111-111111111111", facets.OwnerUserId);
    }

    [Fact]
    public void Catalog_visibility_hides_private_studio_from_non_owners()
    {
        var version = Version("""{"category":"studio","ownerUserId":"owner-a","visibility":"private"}""", "published");
        Assert.True(GccV2StudioTemplateRenderer.IsVisibleInCatalog(version, "owner-a"));
        Assert.False(GccV2StudioTemplateRenderer.IsVisibleInCatalog(version, "owner-b"));
        Assert.False(GccV2StudioTemplateRenderer.CanAccessPublishedStudio(version, "owner-b"));
    }

    [Fact]
    public void Catalog_visibility_includes_admin_shared_published_for_everyone()
    {
        var version = Version(
            """{"category":"studio","ownerUserId":"owner-a","visibility":"admin_shared"}""", "published");
        Assert.True(GccV2StudioTemplateRenderer.IsVisibleInCatalog(version, "owner-b"));
        Assert.True(GccV2StudioTemplateRenderer.CanAccessPublishedStudio(version, "owner-b"));
    }

    [Fact]
    public void Non_studio_agents_remain_visible()
    {
        var version = Version("""{"category":"analysis"}""", "published");
        Assert.True(GccV2StudioTemplateRenderer.IsVisibleInCatalog(version, "anyone"));
        Assert.True(GccV2StudioTemplateRenderer.CanAccessPublishedStudio(version, "anyone"));
    }

    private static GeekAPI.HttpClients.GccV2TaskAgentVersionDto Version(string facetsJson, string state) =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), "1.0.0", "studio", facetsJson,
            "{}", "a", "{}", "b", "{}", "c", "{}", "d", "{}", "e", "{}", "[]", "[]", "[]",
            "{}", "f", "g", state, "actor", null, DateTimeOffset.UtcNow, null, null, null);
}
