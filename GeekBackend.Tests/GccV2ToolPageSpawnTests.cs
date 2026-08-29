using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.ToolPages;

namespace GeekBackend.Tests;

public sealed class GccV2ToolPageSpawnTests
{
    [Fact]
    public void BriefIncludesToolDraft_false_without_contentTypes()
    {
        Assert.False(GccV2ToolPageSpawnService.BriefIncludesToolDraft("{}"));
        Assert.False(GccV2ToolPageSpawnService.BriefIncludesToolDraft("""{"contentTypes":["pillar"]}"""));
    }

    [Fact]
    public void BriefIncludesToolDraft_true_when_tool_checked()
    {
        Assert.True(GccV2ToolPageSpawnService.BriefIncludesToolDraft("""{"contentTypes":["pillar","tool"]}"""));
    }

    [Fact]
    public void MergeOverviewTarget_sets_kind_overview_and_keyword_slug()
    {
        var json = GccV2ToolPageTargetParser.MergeOverviewTarget("{}", "AI Chatbots");
        var target = GccV2ToolPageTargetParser.Parse(json);
        Assert.NotNull(target);
        Assert.True(target!.IsOverview);
        Assert.Equal("ai-chatbots", target.Slug);
        Assert.Equal("/tools/ai-chatbots", target.OnSiteHref);
    }

    [Fact]
    public void ResolveTabLabel_partner_vs_overview()
    {
        var partnerBrief = GccV2ToolPageTargetParser.SerializePartnerBriefSlice(
            "BotPenguin", "bot-penguin", "https://botpenguin.com", null, 1);
        Assert.Equal("Tool · BotPenguin", GccV2ToolPageTargetParser.ResolveTabLabel("tool", partnerBrief));

        var overviewBrief = GccV2ToolPageTargetParser.MergeOverviewTarget("{}", "AI Chatbots");
        Assert.Equal("Tool page", GccV2ToolPageTargetParser.ResolveTabLabel("tool", overviewBrief));
    }

    [Fact]
    public void SpawnResult_not_applicable_for_non_pillar_without_tool()
    {
        var result = new SpawnResult(0, 0, null, null);
        Assert.True(result.NotApplicable);
    }
}
