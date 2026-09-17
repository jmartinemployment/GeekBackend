using GeekAPI.Services.ContentCreatorV2.GeekCrawler;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// A crawl run is binary: it grounds a create only once it has committed. A run still in flight has
/// an arbitrary prefix of its pages, and grounding against a prefix is a wrong answer, not a partial
/// one. EnsureUsableSeedHtml cannot catch this — a single good page satisfies it.
/// </summary>
public sealed class GccV2ProjectSiteCommitGateTests
{
    [Fact]
    public void Complete_run_is_accepted() =>
        GccV2ProjectSiteGrounding.EnsureRunCommitted(Guid.NewGuid(), "complete");

    [Fact]
    public void Status_casing_does_not_decide_the_gate() =>
        GccV2ProjectSiteGrounding.EnsureRunCommitted(Guid.NewGuid(), "Complete");

    [Theory]
    [InlineData("pending")]
    [InlineData("running")]
    [InlineData("external")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("")]
    public void Uncommitted_run_is_refused_and_names_its_status(string status)
    {
        var runId = Guid.NewGuid();
        var ex = Assert.Throws<InvalidOperationException>(
            () => GccV2ProjectSiteGrounding.EnsureRunCommitted(runId, status));

        Assert.Contains(runId.ToString("D"), ex.Message);
        Assert.Contains("not 'complete'", ex.Message);
    }

    [Fact]
    public void In_flight_external_crawl_is_refused()
    {
        // The case this gate exists for: Crawlee is mid-crawl, some pages have landed, and a create
        // starts. Before this gate the create read those pages and grounded against them.
        var ex = Assert.Throws<InvalidOperationException>(
            () => GccV2ProjectSiteGrounding.EnsureRunCommitted(Guid.NewGuid(), "external"));
        Assert.Contains("grounds nothing until it has finished", ex.Message);
    }

    [Fact]
    public void Missing_run_is_refused_rather_than_treated_as_empty()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GccV2ProjectSiteGrounding.EnsureRunCommitted(Guid.NewGuid(), null));
        Assert.Contains("was not found", ex.Message);
    }

    [Fact]
    public void Empty_run_id_is_refused_before_status_is_consulted()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GccV2ProjectSiteGrounding.EnsureRunCommitted(Guid.Empty, "complete"));
        Assert.Contains("Missing required project-site crawl run id", ex.Message);
    }
}
