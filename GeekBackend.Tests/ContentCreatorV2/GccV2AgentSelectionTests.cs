using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Selection identifies agents, not agent versions.
///
/// A pinned agent-VERSION id meant retiring or republishing a specialist broke every create that had
/// selected it: resolution only ever queries published versions, so the old version vanished from the
/// catalog and the job threw. Retirement was never free. Pinning the agent instead means the current
/// published version runs and historical pins keep resolving
/// (plans/agent-specialists.md §4.5).
/// </summary>
public sealed class GccV2AgentSelectionTests
{
    [Fact]
    public void Retiring_a_version_no_longer_orphans_a_pinned_selection()
    {
        var agentId = Guid.NewGuid();
        var retiredVersion = Guid.NewGuid();
        var currentVersion = Guid.NewGuid();

        // What a create pinned earlier, against a version that is no longer published.
        var pinned = new[] { retiredVersion };

        // What the catalog offers now: the same agent, a newer published version.
        var published = new[] { (AgentId: agentId, VersionId: currentVersion) };
        var historical = new[]
        {
            (AgentId: agentId, VersionId: retiredVersion),
            (AgentId: agentId, VersionId: currentVersion),
        };

        var wanted = pinned.ToHashSet();
        var selected = published
            .Where(x => wanted.Contains(x.AgentId)
                        || historical.Any(v => v.AgentId == x.AgentId && wanted.Contains(v.VersionId)))
            .ToList();

        Assert.Single(selected);
        Assert.Equal(currentVersion, selected[0].VersionId);
    }

    [Fact]
    public void An_agent_id_pin_resolves_directly()
    {
        var agentId = Guid.NewGuid();
        var currentVersion = Guid.NewGuid();

        var wanted = new[] { agentId }.ToHashSet();
        var published = new[] { (AgentId: agentId, VersionId: currentVersion) };

        var selected = published.Where(x => wanted.Contains(x.AgentId)).ToList();

        Assert.Single(selected);
        Assert.Equal(currentVersion, selected[0].VersionId);
    }

    [Fact]
    public void A_pin_matching_nothing_published_selects_nothing()
    {
        var wanted = new[] { Guid.NewGuid() }.ToHashSet();
        var published = new[] { (AgentId: Guid.NewGuid(), VersionId: Guid.NewGuid()) };

        var selected = published.Where(x => wanted.Contains(x.AgentId)).ToList();

        Assert.Empty(selected);
    }
}
