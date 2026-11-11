using System.Reflection;
using GeekAPI.Services.Social.Drafting;
using GeekAPI.Services.Social.Orchestration;
using GeekAPI.Services.Social.Triage;
using GeekApplication.Interfaces.Social;

namespace GeekBackend.Tests.Social;

/// <summary>
/// "One responsibility each" enforced by the build rather than asserted in a plan: only Triage and the
/// drafter (and later the post writer) reach the language model, so only they can send a comment out;
/// only the Sender (Phase 4b) may take a platform writer; the orchestrator takes neither; and no agent
/// takes another agent. A new class that breaks one of these fails here, wherever it is put.
/// </summary>
public class SocialAgentSeparationTests
{
    private static readonly Type[] AgentContracts =
    {
        typeof(IEngagementTriage), typeof(IReplyDrafter), typeof(IPostWriter), typeof(IOutboundTextReviewer),
    };

    private static IEnumerable<Type> SocialTypes() =>
        typeof(SocialOrchestrator).Assembly.GetTypes()
            .Where(t => t.IsClass && t.Namespace is not null && t.Namespace.StartsWith("GeekAPI.Services.Social", StringComparison.Ordinal));

    private static IEnumerable<Type> Taken(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);

    private static List<Type> TypesTaking(Type dependency) =>
        SocialTypes().Where(t => Taken(t).Contains(dependency)).ToList();

    [Fact]
    public void Only_triage_and_the_drafter_reach_the_language_model()
    {
        Assert.Equal(
            new[] { typeof(ReplyDrafter), typeof(EngagementTriage) }.OrderBy(t => t.Name),
            TypesTaking(typeof(ISocialModelClient)).OrderBy(t => t.Name));
    }

    [Fact]
    public void Only_the_sender_may_take_a_platform_writer()
    {
        var writers = TypesTaking(typeof(ISocialPublisher)).Concat(TypesTaking(typeof(ISocialEngagementWriter)));

        Assert.All(writers, t => Assert.Equal("GeekAPI.Services.Social.Sending", t.Namespace));
    }

    [Fact]
    public void No_agent_takes_another_agent()
    {
        var agents = SocialTypes().Where(t => AgentContracts.Any(c => c.IsAssignableFrom(t))).ToList();
        Assert.Contains(typeof(EngagementTriage), agents);
        Assert.Contains(typeof(ReplyDrafter), agents);

        foreach (var agent in agents)
            Assert.DoesNotContain(Taken(agent), AgentContracts.Contains);
    }

    [Fact]
    public void The_orchestrator_takes_no_model_and_no_platform()
    {
        var taken = Taken(typeof(SocialOrchestrator)).ToList();

        Assert.DoesNotContain(typeof(ISocialModelClient), taken);
        Assert.DoesNotContain(typeof(ISocialPublisher), taken);
        Assert.DoesNotContain(typeof(ISocialEngagementWriter), taken);
        Assert.DoesNotContain(typeof(ISocialEngagementReader), taken);
        Assert.DoesNotContain(taken, AgentContracts.Contains);
    }
}
