using GeekAPI.HttpClients;
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
    public void ShouldRewakeFailedPartner_true_for_failed_partner_job()
    {
        var brief = GccV2ToolPageTargetParser.SerializePartnerBriefSlice(
            "Pipedrive", "pipedrive", "https://pipedrive.com", null, 1);
        var target = GccV2ToolPageTargetParser.Parse(brief);
        var job = new GccV2JobDto(
            Guid.NewGuid(),
            "tool",
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid(),
            "write",
            "failed",
            1,
            null,
            "WRITE failed",
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

        Assert.True(GccV2ToolPageSpawnService.ShouldRewakeFailedPartner(job, target));
    }

    [Fact]
    public void ShouldRewakeFailedPartner_false_for_overview_job()
    {
        var overviewBrief = GccV2ToolPageTargetParser.MergeOverviewTarget("{}", "AI Chatbots");
        var target = GccV2ToolPageTargetParser.Parse(overviewBrief);
        var job = new GccV2JobDto(
            Guid.NewGuid(),
            "tool",
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid(),
            "write",
            "failed",
            1,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

        Assert.False(GccV2ToolPageSpawnService.ShouldRewakeFailedPartner(job, target));
    }

    [Fact]
    public void SpawnResult_not_applicable_for_non_pillar_without_tool()
    {
        var result = new SpawnResult(0, 0, null, null);
        Assert.True(result.NotApplicable);
    }
}
