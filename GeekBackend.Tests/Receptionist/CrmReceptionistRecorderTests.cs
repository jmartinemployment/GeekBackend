using System.Collections.Concurrent;
using GeekAPI.Services.Receptionist;
using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.Receptionist;

public class CrmReceptionistRecorderTests
{
    private sealed class FakeStore : ICrmStore
    {
        public ConcurrentQueue<object> Writes { get; } = new();
        public bool Fail { get; init; }
        public TimeSpan Delay { get; init; }
        private readonly Guid _participant = Guid.NewGuid();

        private async Task<Result<T>> Answer<T>(object command, T value, CancellationToken ct)
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            Writes.Enqueue(command);
            return Fail ? Result<T>.Failure("down", CrmFailureCodes.StoreUnavailable) : Result<T>.Success(value);
        }

        public Task<Result<EnsureParticipantResult>> EnsureParticipantByPhoneAsync(EnsureParticipantByPhoneCommand command, CancellationToken ct = default) =>
            Answer(command, new EnsureParticipantResult(_participant, true), ct);
        public Task<Result<RecordActivityResult>> RecordActivityAsync(RecordActivityCommand command, CancellationToken ct = default) =>
            Answer(command, new RecordActivityResult(Guid.NewGuid(), true), ct);
        public Task<Result<bool>> SetSmsOptOutAsync(SetSmsOptOutCommand command, CancellationToken ct = default) =>
            Answer(command, true, ct);
        public Task<Result<ImportProspectsResult>> ImportProspectsAsync(ImportProspectsCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<CrmParticipantSummary>>> ListParticipantsAsync(Guid tenantId, string? lifecycleStage, string? kind, int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CrmParticipant>> GetParticipantAsync(Guid tenantId, Guid participantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CrmDeal>> CreateDealAsync(CreateDealCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CrmDeal>> MoveDealStageAsync(Guid dealId, MoveDealStageCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<CrmDeal>>> ListDealsAsync(Guid tenantId, bool openOnly, int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<CrmNaicsCode>>> SearchNaicsAsync(string? query, int? level, int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<CrmPropertyDefinition>>> ListPropertyDefinitionsAsync(Guid tenantId, string? objectType, bool includeRetired, bool includeSensitive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CrmPropertyDefinition>> CreatePropertyDefinitionAsync(CreatePropertyDefinitionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CrmPropertyDefinition>> UpdatePropertyDefinitionAsync(Guid definitionId, UpdatePropertyDefinitionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CrmPropertyDefinition>> RetirePropertyDefinitionAsync(Guid tenantId, Guid definitionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<CrmPropertyValue>>> GetPropertyValuesAsync(Guid tenantId, string objectType, Guid recordId, bool includeSensitive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<CrmPropertyValue>>> SetPropertyValuesAsync(SetPropertyValuesCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static CrmReceptionistRecorder Recorder(FakeStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICrmStore>(store);
        var options = ReceptionistOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TWILIO_ACCOUNT_SID"] = "ACtest", ["TWILIO_AUTH_TOKEN"] = "t", ["TWILIO_API_KEY_SID"] = "SKtest", ["TWILIO_API_KEY_SECRET"] = "s",
            ["TWILIO_NUMBER"] = "+15615550199", ["OWNER_CELL"] = "+15615550100", ["COMPANY_NAME"] = "Dag Plumbing",
            ["PUBLIC_BASE_URL"] = "https://api.example.com",
        }).Build());
        return new CrmReceptionistRecorder(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            options, TimeProvider.System, NullLogger<CrmReceptionistRecorder>.Instance);
    }

    [Fact]
    public async Task A_texted_missed_call_is_a_participant_a_missed_call_and_a_text_back_keyed_by_the_call()
    {
        var store = new FakeStore();
        await Recorder(store).RecordMissedCallAsync("CA1", "+15615551234", MissedCallOutcome.Texted, "Hi", CancellationToken.None);

        var writes = store.Writes.ToArray();
        var ensure = Assert.IsType<EnsureParticipantByPhoneCommand>(writes[0]);
        Assert.Equal(CrmSeed.GeekAtYourSpotTenantId, ensure.TenantId);
        Assert.Equal(ParticipantSources.PhoneCall, ensure.Source);
        var call = Assert.IsType<RecordActivityCommand>(writes[1]);
        Assert.Equal((ActivityChannels.Call, ActivityOutcomes.Missed, "CA1"), (call.Channel, call.Outcome, call.ExternalRef));
        var text = Assert.IsType<RecordActivityCommand>(writes[2]);
        Assert.Equal((ActivityChannels.Sms, ActivityOutcomes.TextedBack, "Hi"), (text.Channel, text.Outcome, text.Body));
    }

    [Fact]
    public async Task A_blocked_caller_is_recorded_without_a_participant()
    {
        var store = new FakeStore();
        await Recorder(store).RecordMissedCallAsync("CA2", "Anonymous", MissedCallOutcome.SkippedNoCallerId, null, CancellationToken.None);

        var call = Assert.IsType<RecordActivityCommand>(Assert.Single(store.Writes));
        Assert.Null(call.ParticipantId);
    }

    [Theory]
    [InlineData("STOP", null, true)]
    [InlineData("anything", "STOP", true)]
    [InlineData("START", null, false)]
    public async Task Stop_and_start_set_the_sms_opt_out(string body, string? optOutType, bool optedOut)
    {
        var store = new FakeStore();
        await Recorder(store).RecordInboundTextAsync("SM1", "+15615551234", body, 0, optOutType, CancellationToken.None);

        var optOut = Assert.IsType<SetSmsOptOutCommand>(store.Writes.Last());
        Assert.Equal(optedOut, optOut.OptedOut);
    }

    [Fact]
    public async Task A_failing_or_slow_store_never_throws_into_the_call()
    {
        await Recorder(new FakeStore { Fail = true }).RecordMissedCallAsync("CA3", "+15615551234", MissedCallOutcome.Texted, "Hi", CancellationToken.None);

        var started = DateTime.UtcNow;
        await Recorder(new FakeStore { Delay = TimeSpan.FromSeconds(30) })
            .RecordVoicemailAsync("CA4", "+15615551234", "https://r", 5, CancellationToken.None);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }
}
