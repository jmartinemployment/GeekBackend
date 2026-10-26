using GeekAPI.Services.Visitors;
using GeekApplication.Models.Visitors;

namespace GeekBackend.Tests.Visitors;

public sealed class VisitorUserAgentParserTests
{
    private readonly VisitorUserAgentParser _parser = new();

    [Fact]
    public void Chrome_on_windows()
    {
        var ua = _parser.Parse(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
            new VisitorClientHints { Mobile = "?0", Platform = "\"Windows\"" });
        Assert.Equal("Chrome", ua.Browser);
        Assert.StartsWith("130", ua.BrowserVersion);
        Assert.Equal("Windows", ua.Os);
        Assert.Equal("desktop", ua.DeviceType);
    }

    [Fact]
    public void Safari_on_iphone()
    {
        var ua = _parser.Parse(
            "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1",
            null);
        Assert.Equal("Mobile Safari", ua.Browser);
        Assert.Equal("iOS", ua.Os);
        Assert.Equal("mobile", ua.DeviceType);
        Assert.Equal("Apple", ua.Brand);
        Assert.Equal("iPhone", ua.Model);
    }

    [Fact]
    public void Samsung_internet_on_android()
    {
        var ua = _parser.Parse(
            "Mozilla/5.0 (Linux; Android 14; SAMSUNG SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0.0.0 Mobile Safari/537.36",
            new VisitorClientHints { Mobile = "?1" });
        Assert.Equal("Samsung Internet", ua.Browser);
        Assert.Equal("Android", ua.Os);
        Assert.Equal("mobile", ua.DeviceType);
        Assert.Equal("Samsung", ua.Brand);
    }

    [Fact]
    public void Edge_on_windows()
    {
        var ua = _parser.Parse(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36 Edg/129.0.2792.79",
            null);
        Assert.Equal("Edge", ua.Browser);
        Assert.Equal("desktop", ua.DeviceType);
    }

    [Fact]
    public void Android_without_mobile_is_a_tablet()
    {
        var ua = _parser.Parse(
            "Mozilla/5.0 (Linux; Android 13; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36",
            null);
        Assert.Equal("tablet", ua.DeviceType);
    }

    [Fact]
    public void Empty_user_agent_keeps_nothing_but_the_unknown_device_type()
    {
        var ua = _parser.Parse("", null);
        Assert.Equal(string.Empty, ua.Raw);
        Assert.Null(ua.Browser);
        Assert.Null(ua.Os);
        Assert.Equal("unknown", ua.DeviceType);
    }
}
