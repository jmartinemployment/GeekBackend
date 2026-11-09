using GeekAPI.Services.Social.Orchestration;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Social;

/// <summary>
/// The orchestrator's intake: it takes a stored event in, recovers what a restart left behind, and
/// reaches only the store. Each test scripts the store, so a call the orchestrator makes that the test
/// did not expect fails the test.
/// </summary>
public class SocialOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);

    private static (SocialOrchestrator Orchestrator, SocialWake Wake, SocialOrchestratorStatus Status, FakeSocialStore Store) Build(
        Action<FakeSocialStore>? script = null)
    {
        var store = new FakeSocialStore
        {
            OnFailInterruptedSends = _ => Result<SocialInterruptedSends>.Success(new SocialInterruptedSends()),
            OnPurge = _ => Result<SocialPurgeResult>.Success(new SocialPurgeResult()),
            OnListEvents = (_, _, _) => Result<IReadOnlyList<EngagementEvent>>.Success(Array.Empty<EngagementEvent>()),
        };
        script?.Invoke(store);

        var services = new ServiceCollection().AddScoped<ISocialStore>(_ => store).BuildServiceProvider();
        var wake = new SocialWake();
        var status = new SocialOrchestratorStatus();
        var orchestrator = new SocialOrchestrator(
            wake, services.GetRequiredService<IServiceScopeFactory>(), status,
            new MetaEngagementReaderTests.FixedClock(Now), NullLogger<SocialOrchestrator>.Instance);
        return (orchestrator, wake, status, store);
    }

    private static EngagementEvent Event(Guid id) => new() { Id = id, Kind = EngagementKinds.CommentCreated };

    private static Result<IReadOnlyList<EngagementEvent>> Events(params Guid[] ids) =>
        Result<IReadOnlyList<EngagementEvent>>.Success(ids.Select(Event).ToArray());

    private static List<Guid> Drain(SocialWake wake)
    {
        var woken = new List<Guid>();
        while (wake.Reader.TryRead(out var id))
            woken.Add(id);
        return woken;
    }

    [Fact]
    public async Task Recovery_fails_interrupted_sends_purges_and_wakes_every_untriaged_comment_oldest_first()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var drafts = new[] { Guid.NewGuid(), Guid.NewGuid() };
        DateTime? purgedAt = null;
        DateTime? failedAt = null;
        string? listedState = null;
        var (orchestrator, wake, status, _) = Build(store =>
        {
            store.OnFailInterruptedSends = at =>
            {
                failedAt = at;
                return Result<SocialInterruptedSends>.Success(new SocialInterruptedSends { DraftIds = drafts.ToList() });
            };
            store.OnPurge = at =>
            {
                purgedAt = at;
                return Result<SocialPurgeResult>.Success(new SocialPurgeResult { EventsScrubbed = 3 });
            };
            store.OnListEvents = (state, _, _) =>
            {
                listedState = state;
                return Events(first, second);
            };
        });

        await orchestrator.RecoverAsync(CancellationToken.None);

        Assert.Equal(Now.UtcDateTime, failedAt);
        Assert.Equal(Now.UtcDateTime, purgedAt);
        Assert.Equal(TriageOutcomes.Untriaged, listedState);
        Assert.Equal(new[] { first, second }, Drain(wake));
        var snapshot = status.Snapshot();
        Assert.Equal(Now.UtcDateTime, snapshot.RecoveredAt);
        Assert.Equal(2, snapshot.EventsWokenAtStartup);
        Assert.Equal(2, snapshot.InterruptedSendsFailed);
        Assert.Equal(3, snapshot.EventsScrubbed);
        Assert.Empty(snapshot.Failures);
    }

    [Fact]
    public async Task Recovery_reads_a_bounded_batch()
    {
        int? limit = null;
        var (orchestrator, _, _, _) = Build(store => store.OnListEvents = (_, _, requested) =>
        {
            limit = requested;
            return Events();
        });

        await orchestrator.RecoverAsync(CancellationToken.None);

        Assert.Equal(SocialOrchestrator.RecoveryBatch, limit);
    }

    [Fact]
    public async Task A_failed_recovery_step_is_recorded_for_the_operator_and_the_other_steps_still_run()
    {
        var pending = Guid.NewGuid();
        var purged = false;
        var (orchestrator, wake, status, _) = Build(store =>
        {
            store.OnFailInterruptedSends = _ =>
                Result<SocialInterruptedSends>.Failure("The store is down.", SocialFailureCodes.StoreUnavailable);
            store.OnPurge = _ =>
            {
                purged = true;
                return Result<SocialPurgeResult>.Success(new SocialPurgeResult());
            };
            store.OnListEvents = (_, _, _) => Events(pending);
        });

        await orchestrator.RecoverAsync(CancellationToken.None);

        Assert.True(purged);
        Assert.Equal(new[] { pending }, Drain(wake));
        var failure = Assert.Single(status.Snapshot().Failures);
        Assert.Equal("fail interrupted sends", failure.Step);
        Assert.Equal(SocialFailureCodes.StoreUnavailable, failure.Code);
        Assert.Equal(Now.UtcDateTime, failure.At);
    }

    [Fact]
    public async Task A_failed_purge_and_a_failed_listing_are_both_recorded_and_wake_nothing()
    {
        var (orchestrator, wake, status, _) = Build(store =>
        {
            store.OnPurge = _ => Result<SocialPurgeResult>.Failure("down", SocialFailureCodes.StoreUnavailable);
            store.OnListEvents = (_, _, _) =>
                Result<IReadOnlyList<EngagementEvent>>.Failure("down", SocialFailureCodes.StoreUnavailable);
        });

        await orchestrator.RecoverAsync(CancellationToken.None);

        Assert.Empty(Drain(wake));
        Assert.Equal(new[] { "purge expired text", "list untriaged events" }, status.Snapshot().Failures.Select(f => f.Step));
    }

    [Fact]
    public async Task Taking_an_event_in_loads_it_and_then_purges()
    {
        var id = Guid.NewGuid();
        Guid? loaded = null;
        var purges = 0;
        var (orchestrator, _, status, _) = Build(store =>
        {
            store.OnGetEvent = requested =>
            {
                loaded = requested;
                return Result<EngagementEvent>.Success(Event(requested));
            };
            store.OnPurge = _ =>
            {
                purges++;
                return Result<SocialPurgeResult>.Success(new SocialPurgeResult { EventsScrubbed = 1 });
            };
        });

        await orchestrator.TakeInAsync(id, CancellationToken.None);

        Assert.Equal(id, loaded);
        Assert.Equal(1, purges);
        var snapshot = status.Snapshot();
        Assert.Equal(1, snapshot.EventsTakenIn);
        Assert.Equal(1, snapshot.EventsScrubbed);
    }

    [Fact]
    public async Task An_event_that_cannot_be_loaded_is_recorded_and_the_purge_still_runs()
    {
        var purged = false;
        var (orchestrator, _, status, _) = Build(store =>
        {
            store.OnGetEvent = _ => Result<EngagementEvent>.Failure("No such event.", SocialFailureCodes.NotFound);
            store.OnPurge = _ =>
            {
                purged = true;
                return Result<SocialPurgeResult>.Success(new SocialPurgeResult());
            };
        });

        await orchestrator.TakeInAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(purged);
        Assert.Equal(0, status.Snapshot().EventsTakenIn);
        var failure = Assert.Single(status.Snapshot().Failures);
        Assert.Equal(("load an event", SocialFailureCodes.NotFound), (failure.Step, failure.Code));
    }

    [Fact]
    public async Task The_service_takes_in_an_event_it_is_woken_with()
    {
        var id = Guid.NewGuid();
        var loaded = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (orchestrator, wake, _, _) = Build(store => store.OnGetEvent = requested =>
        {
            loaded.TrySetResult(requested);
            return Result<EngagementEvent>.Success(Event(requested));
        });

        await orchestrator.StartAsync(CancellationToken.None);
        wake.Wake(id);
        var seen = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await orchestrator.StopAsync(CancellationToken.None);

        Assert.Equal(id, seen);
    }

    [Fact]
    public async Task Recovery_that_failed_at_startup_runs_again_at_the_next_wake()
    {
        var listings = 0;
        var woken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (orchestrator, wake, status, _) = Build(store =>
        {
            store.OnListEvents = (_, _, _) =>
            {
                listings++;
                return listings == 1
                    ? Result<IReadOnlyList<EngagementEvent>>.Failure("not up yet", SocialFailureCodes.StoreUnavailable)
                    : Events();
            };
            store.OnGetEvent = requested =>
            {
                woken.TrySetResult();
                return Result<EngagementEvent>.Success(Event(requested));
            };
        });

        await orchestrator.StartAsync(CancellationToken.None);
        wake.Wake(Guid.NewGuid());
        await woken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await orchestrator.StopAsync(CancellationToken.None);

        Assert.Equal(2, listings);
        Assert.Single(status.Snapshot().Failures);
    }

    [Fact]
    public async Task Recovery_that_completed_is_not_run_again_at_each_wake()
    {
        var listings = 0;
        var woken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (orchestrator, wake, _, _) = Build(store =>
        {
            store.OnListEvents = (_, _, _) =>
            {
                listings++;
                return Events();
            };
            store.OnGetEvent = requested =>
            {
                woken.TrySetResult();
                return Result<EngagementEvent>.Success(Event(requested));
            };
        });

        await orchestrator.StartAsync(CancellationToken.None);
        wake.Wake(Guid.NewGuid());
        await woken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await orchestrator.StopAsync(CancellationToken.None);

        Assert.Equal(1, listings);
    }

    [Fact]
    public async Task A_store_that_throws_on_one_event_does_not_stop_the_next_one_being_taken_in()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var loadedSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (orchestrator, wake, status, _) = Build(store => store.OnGetEvent = requested =>
        {
            if (requested == first)
                throw new InvalidOperationException("the store blew up");
            loadedSecond.TrySetResult();
            return Result<EngagementEvent>.Success(Event(requested));
        });

        await orchestrator.StartAsync(CancellationToken.None);
        wake.Wake(first);
        wake.Wake(second);
        await loadedSecond.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await orchestrator.StopAsync(CancellationToken.None);

        var failure = Assert.Single(status.Snapshot().Failures);
        Assert.Equal(("take in an event", "unexpected_error"), (failure.Step, failure.Code));
    }

    [Fact]
    public async Task A_startup_recovery_that_throws_is_recorded_and_the_service_keeps_running()
    {
        var calls = 0;
        var woken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (orchestrator, wake, status, _) = Build(store =>
        {
            store.OnFailInterruptedSends = _ =>
                ++calls == 1
                    ? throw new InvalidOperationException("boom")
                    : Result<SocialInterruptedSends>.Success(new SocialInterruptedSends());
            store.OnGetEvent = requested =>
            {
                woken.TrySetResult();
                return Result<EngagementEvent>.Success(Event(requested));
            };
        });

        await orchestrator.StartAsync(CancellationToken.None);
        wake.Wake(Guid.NewGuid());
        await woken.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await orchestrator.StopAsync(CancellationToken.None);

        Assert.Equal(("recover at startup", "unexpected_error"), (status.Snapshot().Failures[0].Step, status.Snapshot().Failures[0].Code));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void The_orchestrator_reaches_the_store_and_nothing_else()
    {
        var constructor = Assert.Single(typeof(SocialOrchestrator).GetConstructors());
        var taken = constructor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.Equal(
            new[] { typeof(SocialWake), typeof(IServiceScopeFactory), typeof(SocialOrchestratorStatus), typeof(TimeProvider), typeof(Microsoft.Extensions.Logging.ILogger<SocialOrchestrator>) },
            taken);
        Assert.DoesNotContain(taken, t => t == typeof(ISocialPublisher) || t == typeof(ISocialEngagementWriter) || t == typeof(ISocialEngagementReader));
    }

    [Fact]
    public void The_status_keeps_only_the_most_recent_failures()
    {
        var status = new SocialOrchestratorStatus();
        for (var i = 0; i < 30; i++)
            status.RecordFailure($"step {i}", SocialFailureCodes.StoreUnavailable, Now.UtcDateTime);

        var kept = status.Snapshot().Failures;

        Assert.Equal(20, kept.Count);
        Assert.Equal("step 10", kept[0].Step);
        Assert.Equal("step 29", kept[^1].Step);
    }
}
