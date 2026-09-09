using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GeekBackend.IntegrationTests;

public sealed class GccV2AgentApiContractTests(GeekApiTestFactory factory)
    : IClassFixture<GeekApiTestFactory>
{
    private const string BasePath = "/api/geek-content-creator-v2/agents";
    private const string AgentId = "88888888-8888-8888-8888-888888888888";
    private const string VersionId = "77777777-7777-7777-7777-777777777777";

    [Fact]
    public async Task Catalog_and_detail_expose_pinned_specialist_contract()
    {
        using var client = factory.CreateAuthenticatedClient();
        using var catalogResponse = await client.GetAsync(BasePath + "?contentType=blog");
        catalogResponse.EnsureSuccessStatusCode();
        using var catalog = JsonDocument.Parse(await catalogResponse.Content.ReadAsStringAsync());
        Assert.True(catalog.RootElement.GetProperty("selection").GetProperty("exactlyOneProducer").GetBoolean());
        var writing = catalog.RootElement.GetProperty("agents")[0];
        Assert.Equal("writing", writing.GetProperty("id").GetString());
        Assert.Equal(VersionId, writing.GetProperty("versionId").GetGuid().ToString());
        Assert.Equal("safe-writing", writing.GetProperty("skills")[0].GetProperty("id").GetString());

        using var detailResponse = await client.GetAsync($"{BasePath}/{AgentId}");
        detailResponse.EnsureSuccessStatusCode();
        using var detail = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal("Writing", detail.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("producer",
            detail.RootElement.GetProperty("versions")[0].GetProperty("participation")[0].GetProperty("role").GetString());

        using var resolveResponse = await client.PostAsJsonAsync(BasePath + "/resolve",
            new { selectedAgentIds = new[] { "writing" }, contentTypes = new[] { "blog" } });
        var resolveBody = await resolveResponse.Content.ReadAsStringAsync();
        Assert.True(resolveResponse.IsSuccessStatusCode, resolveBody);
        using var resolved = JsonDocument.Parse(resolveBody);
        Assert.Equal("writing", resolved.RootElement.GetProperty("selectedAgentIds")[0].GetString());
        Assert.Equal(VersionId,
            resolved.RootElement.GetProperty("agents")[0].GetProperty("versionId").GetGuid().ToString());
    }

    [Fact]
    public async Task Admin_test_and_lifecycle_are_authenticated_and_allowlisted()
    {
        using var adminClient = factory.CreateAuthenticatedClient();
        using var workspace = await adminClient.GetAsync(BasePath + "/admin");
        workspace.EnsureSuccessStatusCode();
        using var admin = JsonDocument.Parse(await workspace.Content.ReadAsStringAsync());
        var reloaded = admin.RootElement.GetProperty("agents")[0];
        Assert.True(reloaded.TryGetProperty("objective", out _));
        Assert.True(reloaded.TryGetProperty("skills", out var skills) && skills.GetArrayLength() > 0);
        Assert.True(reloaded.TryGetProperty("participation", out var participation)
            && participation.GetArrayLength() > 0);
        Assert.True(reloaded.TryGetProperty("models", out _));
        Assert.Equal("content-model-policy.v1",
            reloaded.GetProperty("modelPolicy").GetProperty("version").GetString());
        Assert.True(reloaded.TryGetProperty("findings", out _));
        using var test = await adminClient.PostAsync($"{BasePath}/admin/versions/{VersionId}/test", null);
        test.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Accepted, test.StatusCode);
        using var queued = JsonDocument.Parse(await test.Content.ReadAsStringAsync());
        var runId = queued.RootElement.GetProperty("runId").GetGuid();
        Assert.Equal("queued", queued.RootElement.GetProperty("status").GetString());
        using var run = await adminClient.GetAsync($"{BasePath}/admin/test-runs/{runId}");
        run.EnsureSuccessStatusCode();
        using var history = await adminClient.GetAsync($"{BasePath}/admin/versions/{VersionId}/tests");
        history.EnsureSuccessStatusCode();
        using var publish = await adminClient.PostAsync($"{BasePath}/admin/versions/{VersionId}/publish", null);
        publish.EnsureSuccessStatusCode();
        using var revoke = await adminClient.PostAsJsonAsync(
            $"{BasePath}/admin/versions/{VersionId}/revoke", new { reason = "contract test" });
        revoke.EnsureSuccessStatusCode();

        using var nonAdmin = factory.CreateAuthenticatedClient(GeekApiTestFactory.OtherUserId);
        using var denied = await nonAdmin.GetAsync(BasePath + "/admin");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedRun = await nonAdmin.GetAsync($"{BasePath}/admin/test-runs/{runId}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedRun.StatusCode);
    }
}
