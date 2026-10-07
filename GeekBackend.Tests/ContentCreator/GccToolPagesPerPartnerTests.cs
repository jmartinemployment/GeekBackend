using GeekAPI.Services.ContentCreator;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekAPI.Services.Workflow.Domain.Enums;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Five tool pages from one generate, each about one partner's product — and the same unit for a
/// single page (Jeff, 2026-10-02: both, priority on sets of five).
/// </summary>
/// <remarks>
/// These assert the fan-out's contract through the real service. The per-slice mechanics — bucketing,
/// naming, narrowing — are pinned in <see cref="GccPartnerToolSlicesTests"/>; what matters here is that
/// one partner refusing does not take the others with it.
/// </remarks>
public class GccToolPagesPerPartnerTests
{
    [Fact]
    public async Task No_declared_partners_refuses_once_rather_than_per_partner()
    {
        // A project with no partner URLs cannot have tool pages at all. That is one statement about the
        // project, not five identical ones about products that do not exist.
        var fixtures = GccToolPageFanOutFixture.WithPartners();

        var outcomes = await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        var only = Assert.Single(outcomes);
        Assert.False(only.Written);
        Assert.Contains("declares no partner URLs", only.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_product_this_project_declares_no_partner_for_is_refused_by_name()
    {
        var fixtures = GccToolPageFanOutFixture.WithPartners("https://dext.com", "https://bill.com");

        var outcomes = await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None,
            onlyProduct: "Notion");

        var only = Assert.Single(outcomes);
        Assert.False(only.Written);
        Assert.Equal("Notion", only.ProductName);
        Assert.Contains("not one of this project's declared partners", only.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_declared_partner_gets_an_outcome_named_for_its_product()
    {
        // Five in, five out — written or refused, never silently absent. A declared partner that
        // disappears with no error is the degrade this codebase refuses everywhere.
        var fixtures = GccToolPageFanOutFixture.WithPartners(
            "https://dext.com", "https://bill.com", "https://avidxchange.com",
            "https://stampli.com", "https://melio.com");

        var outcomes = await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.Equal(5, outcomes.Count);
        Assert.Equal(
            ["Dext", "Bill", "Avidxchange", "Stampli", "Melio"],
            outcomes.Select(o => o.ProductName));
        // Each refusal names its own product, so four pages and one gap is readable.
        Assert.All(outcomes, o => Assert.False(string.IsNullOrWhiteSpace(o.ProductName)));
    }

    [Fact]
    public async Task One_partner_refusing_does_not_stop_the_others_being_attempted()
    {
        // Every partner is attempted and every outcome comes back; the caller decides what to persist.
        var fixtures = GccToolPageFanOutFixture.WithPartners(
            "https://dext.com", "https://melio.com", "https://bill.com");

        var outcomes = await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.Equal(3, outcomes.Count);
        Assert.All(outcomes, o => Assert.True(o.Written || o.Refusal is not null));
    }

    [Fact]
    public async Task A_single_product_is_the_same_path_narrowed_to_one()
    {
        var fixtures = GccToolPageFanOutFixture.WithPartners("https://dext.com", "https://bill.com");

        var outcomes = await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None,
            onlyProduct: "Dext");

        var only = Assert.Single(outcomes);
        Assert.Equal("Dext", only.ProductName);
    }
}
