using System.Net;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2SafeOutboundUrlTests
{
    private static readonly IPAddress PublicExample = IPAddress.Parse("93.184.216.34");

    private static GccV2SafeOutboundUrl.HostResolver PublicOnly =>
        _ => [PublicExample];

    [Theory]
    [InlineData("https://example.com/path", "example.com")]
    [InlineData("http://example.com/", "example.com")]
    [InlineData("https://cdn.example.com:443/a", "cdn.example.com")]
    public void Accepts_public_http_https_hosts(string url, string expectedHost)
    {
        Assert.True(GccV2SafeOutboundUrl.TryValidate(url, out var absolute, out var reason, PublicOnly));
        Assert.Null(reason);
        Assert.Equal(expectedHost, absolute.Host, ignoreCase: true);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData("not a url")]
    public void Rejects_non_http_schemes_and_garbage(string url)
    {
        Assert.False(GccV2SafeOutboundUrl.TryValidate(url, out _, out var reason, PublicOnly));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://user:pass@example.com/")]
    [InlineData("http://metadata.google.internal/")]
    [InlineData("http://app.localhost/")]
    public void Rejects_ssrf_targets(string url)
    {
        Assert.False(GccV2SafeOutboundUrl.TryValidate(url, out _, out var reason, PublicOnly));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Rejects_host_that_resolves_to_private_address()
    {
        GccV2SafeOutboundUrl.HostResolver privateResolve = _ => [IPAddress.Parse("10.1.2.3")];
        Assert.False(GccV2SafeOutboundUrl.TryValidate(
            "https://evil.example/", out _, out var reason, privateResolve));
        Assert.Contains("blocked address", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_ipv4_mapped_ipv6_loopback()
    {
        Assert.True(GccV2SafeOutboundUrl.IsBlockedAddress(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.True(GccV2SafeOutboundUrl.IsBlockedAddress(IPAddress.Parse("::ffff:169.254.169.254")));
        Assert.False(GccV2SafeOutboundUrl.IsBlockedAddress(PublicExample));
    }

    [Fact]
    public async Task PageFetcher_does_not_navigate_when_url_is_blocked()
    {
        await using var holder = new GccV2PlaywrightBrowserHolder();
        // Do not initialize Playwright — SSRF gate must short-circuit first.
        var fetcher = new GccV2PageFetcher(holder, NullLogger<GccV2PageFetcher>.Instance);

        var result = await fetcher.FetchAsync("http://127.0.0.1/", CancellationToken.None);

        Assert.Null(result);
        Assert.Null(holder.Browser);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5/internal")]
    public void Post_redirect_targets_are_also_blocked_by_the_same_gate(string finalUrl)
    {
        // PageFetcher re-validates response.Url / page.Url with this gate after GotoAsync.
        Assert.False(GccV2SafeOutboundUrl.TryValidate(finalUrl, out _, out var reason, PublicOnly));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}
