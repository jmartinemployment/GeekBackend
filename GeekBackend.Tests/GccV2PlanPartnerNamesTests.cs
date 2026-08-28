using GeekAPI.Services.ContentCreatorV2.Plan;

namespace GeekBackend.Tests;

public sealed class GccV2PlanPartnerNamesTests
{
    [Fact]
    public void ExtractPartnerToolNames_uses_recommended_only_and_rejects_urls()
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

        Assert.Single(names);
        Assert.Equal("Mailchimp", names[0]);
    }
}
