using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GeekBackend.IntegrationTests;

public sealed class GccV2TaskAgentApiContractTests(GeekApiTestFactory factory)
    : IClassFixture<GeekApiTestFactory>
{
    private const string BasePath = "/api/geek-content-creator-v2/task-agents";

    [Fact]
    public async Task Catalog_run_result_and_cancel_preserve_owner_scoped_pins()
    {
        using var client = factory.CreateAuthenticatedClient();
        using var catalogResponse = await client.GetAsync(BasePath);
        catalogResponse.EnsureSuccessStatusCode();
        using var catalog = JsonDocument.Parse(await catalogResponse.Content.ReadAsStringAsync());
        var taskAgent = catalog.RootElement.GetProperty("agents")[0];
        Assert.Equal("fact-density", taskAgent.GetProperty("id").GetString());
        Assert.Equal(64, taskAgent.GetProperty("digest").GetString()!.Length);

        using var invalid = await client.PostAsJsonAsync(
            BasePath + "/fact-density/runs", new { input = new { wrong = true } });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var create = await client.PostAsJsonAsync(
            BasePath + "/fact-density/runs", new { input = new { topic = "AI search" } });
        var createBody = await create.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        using var accepted = JsonDocument.Parse(createBody);
        var runId = accepted.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("queued", accepted.RootElement.GetProperty("status").GetString());

        using var resultResponse = await client.GetAsync($"{BasePath}/runs/{runId}/result");
        resultResponse.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await resultResponse.Content.ReadAsStringAsync());
        Assert.Equal("gcc-task-result-shell.v1",
            result.RootElement.GetProperty("contractVersion").GetString());
        Assert.Equal("fact-density",
            result.RootElement.GetProperty("identity").GetProperty("capabilityId").GetString());
        Assert.Equal(runId,
            result.RootElement.GetProperty("snapshot").GetProperty("rootRunId").GetGuid());

        using var other = factory.CreateAuthenticatedClient(GeekApiTestFactory.OtherUserId);
        using var hidden = await other.GetAsync($"{BasePath}/runs/{runId}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        using var cancel = await client.PostAsync($"{BasePath}/runs/{runId}/cancel", null);
        cancel.EnsureSuccessStatusCode();
        using var cancelled = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync());
        Assert.Equal("cancelled", cancelled.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Admin_lifecycle_reuses_existing_allowlist()
    {
        using var admin = factory.CreateAuthenticatedClient();
        using var workspace = await admin.GetAsync(BasePath + "/admin");
        workspace.EnsureSuccessStatusCode();
        using var definitions = JsonDocument.Parse(await workspace.Content.ReadAsStringAsync());
        var versionId = definitions.RootElement[0].GetProperty("versions")[0].GetProperty("id").GetGuid();
        using var transition = await admin.PostAsJsonAsync(
            $"{BasePath}/admin/versions/{versionId}/deprecate", new { reason = "contract" });
        transition.EnsureSuccessStatusCode();

        using var nonAdmin = factory.CreateAuthenticatedClient(GeekApiTestFactory.OtherUserId);
        using var denied = await nonAdmin.GetAsync(BasePath + "/admin");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
