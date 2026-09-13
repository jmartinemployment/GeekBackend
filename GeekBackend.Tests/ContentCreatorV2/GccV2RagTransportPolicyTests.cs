using GeekAPI.Services.ContentCreatorV2.Context;

namespace GeekBackend.Tests.ContentCreatorV2;

public class GccV2RagTransportPolicyTests
{
    [Fact]
    public void Https_is_always_allowed()
    {
        Assert.True(GccV2RagTransportPolicy.IsAllowedBaseAddress(new Uri("https://rag.geekatyourspot.com")));
    }

    [Fact]
    public void Localhost_http_is_allowed()
    {
        Assert.True(GccV2RagTransportPolicy.IsAllowedBaseAddress(new Uri("http://127.0.0.1:8080")));
        Assert.True(GccV2RagTransportPolicy.IsAllowedBaseAddress(new Uri("http://localhost:8080")));
    }

    [Fact]
    public void Plaintext_ip_requires_allowlist()
    {
        var previous = Environment.GetEnvironmentVariable(GccV2RagTransportPolicy.InsecureHttpHostsEnv);
        try
        {
            Environment.SetEnvironmentVariable(GccV2RagTransportPolicy.InsecureHttpHostsEnv, null);
            Assert.False(GccV2RagTransportPolicy.IsAllowedBaseAddress(new Uri("http://2.24.101.90:8080")));

            Environment.SetEnvironmentVariable(GccV2RagTransportPolicy.InsecureHttpHostsEnv, "2.24.101.90");
            Assert.True(GccV2RagTransportPolicy.IsAllowedBaseAddress(new Uri("http://2.24.101.90:8080")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(GccV2RagTransportPolicy.InsecureHttpHostsEnv, previous);
        }
    }
}
