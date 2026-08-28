using GeekAPI.Services.ContentCreatorV2.Adapters;

namespace GeekBackend.Tests;

public sealed class GccV2PartnerToolsTests
{
    [Fact]
    public void ToolsForWritingNotes_uses_recommended_only_and_absolutizes_on_site_href()
    {
        var recommended = new GccV2ContextAdapter.RecommendedTool[]
        {
            new("Mailchimp", "/tools/marketing/mailchimp"),
            new("Mailchimp", "https://mailchimp.com"),
        };

        var tools = GccV2ContextAdapter.ToolsForWritingNotes(
            recommended,
            "https://geekatyourspot.com",
            "https://geekatyourspot.com/tools");

        Assert.Single(tools);
        Assert.Equal("Mailchimp", tools[0].Name);
        Assert.Equal("https://geekatyourspot.com/tools/marketing/mailchimp", tools[0].Href);
    }

    [Fact]
    public void ToolsForWritingNotes_drops_off_site_href()
    {
        var recommended = new GccV2ContextAdapter.RecommendedTool[]
        {
            new("ManyChat", "https://manychat.com"),
        };

        var tools = GccV2ContextAdapter.ToolsForWritingNotes(
            recommended,
            "https://geekatyourspot.com",
            null);

        Assert.Single(tools);
        Assert.Equal("ManyChat", tools[0].Name);
        Assert.Null(tools[0].Href);
    }

    [Fact]
    public void HrefLooksLikeOnSiteToolPage_recognizes_relative_and_absolute()
    {
        Assert.True(GccV2ContextAdapter.HrefLooksLikeOnSiteToolPage("/tools/marketing/mailchimp"));
        Assert.True(GccV2ContextAdapter.HrefLooksLikeOnSiteToolPage("https://geekatyourspot.com/tools/marketing/mailchimp"));
        Assert.False(GccV2ContextAdapter.HrefLooksLikeOnSiteToolPage("https://mailchimp.com"));
    }
}
