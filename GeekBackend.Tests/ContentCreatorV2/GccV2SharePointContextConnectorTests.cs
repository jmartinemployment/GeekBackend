using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.ContentCreatorV2.SharePoint;
using Microsoft.Extensions.DependencyInjection;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2SharePointContextConnectorTests
{
    [Fact]
    public void Registry_lists_sharepoint_connector()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGccV2ContextConnector, GccV2UrlContextConnector>();
        services.AddSingleton<IGccV2ContextConnector, GccV2GscContextConnector>();
        services.AddSingleton<IGccV2ContextConnector, GccV2DriveContextConnector>();
        services.AddSingleton<IGccV2ContextConnector, GccV2SharePointContextConnector>();
        services.AddSingleton<GccV2ContextConnectorRegistry>();
        services.AddSingleton<IServiceScopeFactory, StubScopeFactory>();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<GccV2ContextConnectorRegistry>();
        Assert.Contains("sharepoint", registry.ConnectorIds);
        Assert.Equal("sharepoint", registry.Resolve("sharepoint").Id);
    }

    [Fact]
    public void CanRefresh_requires_connection_and_item()
    {
        var connector = new GccV2SharePointContextConnector(new StubScopeFactory());
        using var ok = JsonDocument.Parse(
            """{"connectorId":"sharepoint","sharePointConnectionId":"11111111-1111-4111-8111-111111111111","itemId":"01ABCDEFGHIJKLMNOPQR"}""");
        using var missingItem = JsonDocument.Parse(
            """{"connectorId":"sharepoint","sharePointConnectionId":"11111111-1111-4111-8111-111111111111"}""");
        using var other = JsonDocument.Parse(
            """{"connectorId":"drive","sharePointConnectionId":"11111111-1111-4111-8111-111111111111","itemId":"01ABCDEFGHIJKLMNOPQR"}""");

        Assert.True(connector.CanRefresh(ok.RootElement));
        Assert.False(connector.CanRefresh(missingItem.RootElement));
        Assert.False(connector.CanRefresh(other.RootElement));
    }

    [Theory]
    [InlineData("01ABCDEFGHIJKLMNOPQRSTUV", false)]
    [InlineData("https://contoso.sharepoint.com/:w:/s/Team/abc123", true)]
    [InlineData("https://onedrive.live.com/?id=ABC&cid=DEF", true)]
    public void TryNormalizeItemRef_accepts_id_and_share_urls(string input, bool expectShareUrl)
    {
        Assert.True(GccV2SharePointGraphClient.TryNormalizeItemRef(input, out var itemRef, out var isShare));
        Assert.Equal(expectShareUrl, isShare);
        Assert.False(string.IsNullOrWhiteSpace(itemRef));
    }

    [Fact]
    public void TryNormalizeItemRef_rejects_empty_and_non_microsoft_hosts()
    {
        Assert.False(GccV2SharePointGraphClient.TryNormalizeItemRef("", out _, out _));
        Assert.False(GccV2SharePointGraphClient.TryNormalizeItemRef("https://example.com/x", out _, out _));
        Assert.False(GccV2SharePointGraphClient.TryNormalizeItemRef("short", out _, out _));
    }

    [Fact]
    public void ToShareId_uses_u_bang_base64url()
    {
        var shareId = GccV2SharePointGraphClient.ToShareId("https://contoso.sharepoint.com/x");
        Assert.StartsWith("u!", shareId);
        Assert.DoesNotContain("+", shareId);
        Assert.DoesNotContain("/", shareId.AsSpan(2));
    }

    private sealed class StubScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new StubScope();

        private sealed class StubScope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();
            public void Dispose() { }
        }
    }
}
