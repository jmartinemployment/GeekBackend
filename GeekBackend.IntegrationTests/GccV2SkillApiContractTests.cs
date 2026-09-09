using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GeekBackend.IntegrationTests;

public sealed class GccV2SkillApiContractTests(GeekApiTestFactory factory)
    : IClassFixture<GeekApiTestFactory>
{
    private const string BasePath = "/api/geek-content-creator-v2/skills";
    private const string VersionId = "44444444-4444-4444-4444-444444444444";
    private const string FindingId = "55555555-5555-5555-5555-555555555555";

    [Fact]
    public async Task Catalog_and_resolve_match_frontend_contract()
    {
        using var client = factory.CreateAuthenticatedClient();
        using var catalogResponse = await client.GetAsync(BasePath + "?contentType=blog");
        catalogResponse.EnsureSuccessStatusCode();
        using var catalog = JsonDocument.Parse(await catalogResponse.Content.ReadAsStringAsync());
        var summary = catalog.RootElement.GetProperty("skills")[0];
        Assert.Equal("safe-writing", summary.GetProperty("id").GetString());
        Assert.Equal("first-party", summary.GetProperty("origin").GetString());
        Assert.Equal("load_evidence_page", summary.GetProperty("requestedTools")[0].GetString());

        using var resolveResponse = await client.GetAsync(BasePath + "/resolve?contentTypes=blog,landing-page");
        resolveResponse.EnsureSuccessStatusCode();
        using var resolved = JsonDocument.Parse(await resolveResponse.Content.ReadAsStringAsync());
        Assert.Equal("gcc-skill-resolution.v2", resolved.RootElement.GetProperty("snapshotVersion").GetString());
        Assert.Equal(64, resolved.RootElement.GetProperty("snapshotDigest").GetString()!.Length);
        Assert.Equal("safe-writing", resolved.RootElement.GetProperty("skills")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Admin_workspace_and_mutation_aliases_match_frontend_contract()
    {
        using var client = factory.CreateAuthenticatedClient();
        using var workspaceResponse = await client.GetAsync(BasePath + "/admin");
        workspaceResponse.EnsureSuccessStatusCode();
        using var workspace = JsonDocument.Parse(await workspaceResponse.Content.ReadAsStringAsync());
        Assert.True(workspace.RootElement.GetProperty("authorized").GetBoolean());
        var skill = workspace.RootElement.GetProperty("skills")[0];
        Assert.Equal(VersionId, skill.GetProperty("versionId").GetGuid().ToString());
        Assert.True(skill.TryGetProperty("findings", out _));
        Assert.True(skill.TryGetProperty("files", out _));
        Assert.True(skill.TryGetProperty("audit", out _));

        using var finding = await client.PatchAsJsonAsync(
            $"{BasePath}/admin/{VersionId}/findings/{FindingId}",
            new { disposition = "resolved", reviewerRationale = "Reviewed in integration test" });
        finding.EnsureSuccessStatusCode();
        using var review = await client.PostAsJsonAsync(
            $"{BasePath}/admin/{VersionId}/review", new { decision = "approve", notes = "Reviewed" });
        review.EnsureSuccessStatusCode();
        using var publish = await client.PostAsync($"{BasePath}/admin/{VersionId}/publish", null);
        publish.EnsureSuccessStatusCode();
        using var deprecate = await client.PostAsJsonAsync(
            $"{BasePath}/admin/{VersionId}/deprecate", new { reason = "Superseded" });
        deprecate.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Admin_workspace_rejects_non_admin()
    {
        using var client = factory.CreateAuthenticatedClient(GeekApiTestFactory.OtherUserId);
        using var response = await client.GetAsync(BasePath + "/admin");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
