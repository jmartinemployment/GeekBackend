using System.Reflection;
using System.Text.Json;
using GeekAPI.Controllers.ContentCreatorV2;
using GeekAPI.Services.ContentCreatorV2.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2GscContextConnectorTests
{
    [Fact]
    public void Registry_lists_gsc_connector()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServiceScopeFactory, StubScopeFactory>();
        services.AddSingleton<IGccV2ContextConnector, GccV2GscContextConnector>();
        services.AddSingleton<GccV2ContextConnectorRegistry>();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<GccV2ContextConnectorRegistry>();

        Assert.Contains(GccV2GscContextConnector.ConnectorId, registry.ConnectorIds);
        Assert.Equal(GccV2GscContextConnector.ConnectorId, registry.Resolve("gsc").Id);
    }

    [Fact]
    public void CanRefresh_requires_gsc_connection_id()
    {
        var connector = new GccV2GscContextConnector(new StubScopeFactory());
        using var ok = JsonDocument.Parse(
            """{"connectorId":"gsc","gscConnectionId":"11111111-1111-4111-8111-111111111111"}""");
        using var missing = JsonDocument.Parse("""{"connectorId":"gsc"}""");
        using var other = JsonDocument.Parse(
            """{"connectorId":"url","gscConnectionId":"11111111-1111-4111-8111-111111111111"}""");

        Assert.True(connector.CanRefresh(ok.RootElement));
        Assert.False(connector.CanRefresh(missing.RootElement));
        Assert.False(connector.CanRefresh(other.RootElement));
    }

    [Fact]
    public async Task FetchAsync_requires_gsc_connection_id()
    {
        var connector = new GccV2GscContextConnector(new StubScopeFactory());
        using var descriptor = JsonDocument.Parse("""{"connectorId":"gsc"}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.FetchAsync(descriptor.RootElement, "owner", CancellationToken.None));

        Assert.Contains("gscConnectionId", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromGsc_route_is_registered()
    {
        var method = typeof(GccV2ContextController).GetMethod(
            nameof(GccV2ContextController.CreateKnowledgeFromGsc));
        Assert.NotNull(method);
        var route = method!.GetCustomAttribute<HttpPostAttribute>();
        Assert.Equal("knowledge/from-gsc", route?.Template);
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
