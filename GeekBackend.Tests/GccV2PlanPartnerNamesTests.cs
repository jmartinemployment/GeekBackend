using GeekAPI.Services.ContentCreatorV2.Plan;

namespace GeekBackend.Tests;

public sealed class GccV2PlanPartnerNamesTests
{
    [Fact]
    public void ExtractPartnerToolNames_includes_recommended_and_operator_tools_rejects_urls()
    {
        const string briefJson = """
            {
              "hierarchyPlan": {
                "recommendedTools": [
                  { "name": "Mailchimp", "href": "/tools/marketing/mailchimp" },
                  { "name": "https://manychat.com", "href": "/tools/marketing/manychat" }
                ]
              },
              "operatorTools": [
                { "name": "BotPenguin", "url": "https://botpenguin.com/" }
              ]
            }
            """;

        var names = GccV2PlanService.ExtractPartnerToolNames(briefJson);

        Assert.Equal(2, names.Count);
        Assert.Contains("Mailchimp", names);
        Assert.Contains("BotPenguin", names);
        Assert.DoesNotContain(names, n => n.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    }
}
