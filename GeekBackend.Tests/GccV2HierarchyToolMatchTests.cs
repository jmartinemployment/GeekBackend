using GeekAPI.Services.ContentCreatorV2.Hierarchy;

namespace GeekBackend.Tests;

public class GccV2HierarchyToolMatchTests
{
    private static GccV2SiteHierarchy FixtureTree()
    {
        var chatbots = new GccV2HeadingNode(
            5,
            "Smart Chatbots for Marketing:",
            [],
            [
                new GccV2HeadingLink("Fin.ai", "/tools/fin"),
                new GccV2HeadingLink("Intercom", "/tools/intercom"),
                new GccV2HeadingLink("Drift", "/tools/drift"),
            ],
            []);

        var marketing = new GccV2HeadingNode(
            3,
            "Marketing",
            [],
            [
                new GccV2HeadingLink("Buffer", "/tools/buffer"),
                new GccV2HeadingLink("Hootsuite", "/tools/hootsuite"),
                new GccV2HeadingLink("SocialBee", "/tools/socialbee"),
                new GccV2HeadingLink("CoSchedule", "/tools/coschedule"),
                new GccV2HeadingLink("SocialPilot", "/tools/socialpilot"),
                new GccV2HeadingLink("Zoho Social", "/tools/zoho"),
                new GccV2HeadingLink("HubSpot", "/tools/hubspot"),
            ],
            [chatbots]);

        return new GccV2SiteHierarchy(
            "https://geekatyourspot.com/",
            "mobile",
            DateTimeOffset.UtcNow,
            [new GccV2PageHierarchy("https://geekatyourspot.com/", [marketing])]);
    }

    [Fact]
    public void Smart_Chatbots_Keyword_Does_Not_Pick_Marketing_Social_Tools()
    {
        var match = GccV2HierarchyToolMatch.Match(
            FixtureTree(),
            ["Smart Chatbots for Marketing"]);

        Assert.NotNull(match);
        Assert.Contains("Smart Chatbots", match!.MatchedHeading, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            match.RecommendedTools,
            t => t.Name.Equals("Buffer", StringComparison.OrdinalIgnoreCase)
                 || t.Name.Equals("Hootsuite", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(match.RecommendedTools, t => t.Name.Equals("Fin.ai", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(match.RecommendedTools, t => t.Name.Equals("Intercom", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Smart Chatbots for Marketing", match.MatchTopic);
        Assert.Contains(match.Path, p => p.Contains("Smart Chatbots", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Exact_Marketing_Still_Gets_Social_Tools()
    {
        var match = GccV2HierarchyToolMatch.Match(FixtureTree(), ["Marketing"]);

        Assert.NotNull(match);
        Assert.Equal("Marketing", match!.MatchedHeading);
        Assert.Contains(match.RecommendedTools, t => t.Name.Equals("Buffer", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(7, match.RecommendedTools.Count);
    }

    [Fact]
    public void ExpandSeeds_Does_Not_Emit_Lone_Marketing()
    {
        var expanded = GccV2HierarchyToolMatch.ExpandSeeds(["Smart Chatbots for Marketing"]).ToList();
        Assert.Contains("Smart Chatbots for Marketing", expanded);
        Assert.Contains("Smart Chatbots", expanded);
        Assert.DoesNotContain(expanded, s => s.Equals("Marketing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Score_Rejects_Parent_Contains()
    {
        Assert.Null(GccV2HierarchyToolMatch.Score("Marketing", "Smart Chatbots for Marketing"));
        Assert.Equal(
            "exact-heading",
            GccV2HierarchyToolMatch.Score("Smart Chatbots for Marketing:", "Smart Chatbots for Marketing"));
        // Trailing colon is normalized away → exact, not near-exact.
        Assert.Equal(
            "exact-heading",
            GccV2HierarchyToolMatch.Score("Smart Chatbots:", "Smart Chatbots"));
        Assert.Equal(
            "near-exact-heading",
            GccV2HierarchyToolMatch.Score("Smart Chatbots for Marketing", "Smart Chatbots"));
    }
}
