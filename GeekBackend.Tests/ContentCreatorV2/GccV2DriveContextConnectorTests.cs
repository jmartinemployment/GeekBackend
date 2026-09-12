using System.Text.Json;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.ContentCreatorV2.Drive;
using Microsoft.Extensions.DependencyInjection;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2DriveContextConnectorTests
{
    [Fact]
    public void Registry_lists_drive_connector()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGccV2ContextConnector, GccV2UrlContextConnector>();
        services.AddSingleton<IGccV2ContextConnector, GccV2GscContextConnector>();
        services.AddSingleton<IGccV2ContextConnector, GccV2DriveContextConnector>();
        services.AddSingleton<GccV2ContextConnectorRegistry>();
        services.AddSingleton<IServiceScopeFactory, StubScopeFactory>();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<GccV2ContextConnectorRegistry>();
        Assert.Contains("drive", registry.ConnectorIds);
        Assert.Equal("drive", registry.Resolve("drive").Id);
    }

    [Fact]
    public void CanRefresh_requires_drive_connection_and_file()
    {
        var connector = new GccV2DriveContextConnector(new StubScopeFactory());
        using var ok = JsonDocument.Parse(
            """{"connectorId":"drive","driveConnectionId":"11111111-1111-4111-8111-111111111111","fileId":"abc123XYZ_-"}""");
        using var missingFile = JsonDocument.Parse(
            """{"connectorId":"drive","driveConnectionId":"11111111-1111-4111-8111-111111111111"}""");
        using var other = JsonDocument.Parse(
            """{"connectorId":"gsc","driveConnectionId":"11111111-1111-4111-8111-111111111111","fileId":"abc"}""");

        Assert.True(connector.CanRefresh(ok.RootElement));
        Assert.False(connector.CanRefresh(missingFile.RootElement));
        Assert.False(connector.CanRefresh(other.RootElement));
    }

    [Theory]
    [InlineData("1a2b3c4d5e6f7g8h9i0j", "1a2b3c4d5e6f7g8h9i0j")]
    [InlineData("https://drive.google.com/file/d/1a2b3c4d5e6f7g8h9i0j/view", "1a2b3c4d5e6f7g8h9i0j")]
    [InlineData("https://docs.google.com/document/d/1a2b3c4d5e6f7g8h9i0j/edit", "1a2b3c4d5e6f7g8h9i0j")]
    [InlineData("https://drive.google.com/open?id=1a2b3c4d5e6f7g8h9i0j", "1a2b3c4d5e6f7g8h9i0j")]
    public void TryParseFileId_accepts_id_and_common_urls(string input, string expected)
    {
        Assert.True(GccV2DriveFilesClient.TryParseFileId(input, out var fileId));
        Assert.Equal(expected, fileId);
    }

    [Fact]
    public void TryParseFileId_rejects_empty_and_junk()
    {
        Assert.False(GccV2DriveFilesClient.TryParseFileId("", out _));
        Assert.False(GccV2DriveFilesClient.TryParseFileId("https://example.com/x", out _));
        Assert.False(GccV2DriveFilesClient.TryParseFileId("short", out _));
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
