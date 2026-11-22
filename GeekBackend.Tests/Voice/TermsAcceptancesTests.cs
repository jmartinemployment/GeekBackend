using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// The Terms are accepted after the receptionist's setup and before the plan (Jeff, 2026-11-22). Only the exact words, only for a business
/// still waiting for payment, only from a visitor whose location was verified; stored once, and asking again is not an error.
/// </summary>
public sealed class TermsAcceptancesTests
{
    private static readonly Guid Tenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 15, 0, 0, TimeSpan.Zero);
    private static readonly VisitorOrigin FromUs = new("203.0.113.9", "US");

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static TermsAcceptances Gate(RecordingSignupAnswerStore answers, string status = VoiceTenantStatuses.PendingPayment) =>
        new(answers, new PendingTenantStore(status), new Clock());

    private static AcceptTermsRequest Accept(string? words = null) => new(Tenant, words ?? SignupStatements.Terms);

    [Fact]
    public async Task The_exact_words_are_stored_with_when_and_where_they_were_accepted()
    {
        var answers = new RecordingSignupAnswerStore();

        var result = await Gate(answers).AcceptAsync(Accept(), FromUs);

        Assert.True(result.IsSuccess, result.Error);
        var kept = Assert.Single(answers.Acceptances);
        Assert.Equal(Tenant, kept.TenantId);
        Assert.Equal(SignupStatements.Terms, kept.Statement);
        Assert.Equal(Now, kept.AcceptedAt);
        Assert.Equal("203.0.113.9", kept.IpAddress);
        Assert.Equal("US", kept.VisitorCountry);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I agree to something else.")]
    public async Task Any_other_words_are_refused_and_nothing_is_stored(string? words)
    {
        var answers = new RecordingSignupAnswerStore();

        var result = await Gate(answers).AcceptAsync(new AcceptTermsRequest(Tenant, words), FromUs);

        Assert.Equal(OnboardingFailureCodes.TermsRequired, result.ErrorCode);
        Assert.Empty(answers.Acceptances);
    }

    [Fact]
    public async Task A_visitor_whose_location_could_not_be_verified_is_refused()
    {
        var answers = new RecordingSignupAnswerStore();

        var result = await Gate(answers).AcceptAsync(Accept(), new VisitorOrigin("", ""));

        Assert.Equal(OnboardingFailureCodes.VisitorUnverified, result.ErrorCode);
        Assert.Empty(answers.Acceptances);
    }

    [Theory]
    [InlineData(VoiceTenantStatuses.Active)]
    [InlineData(VoiceTenantStatuses.Suspended)]
    public async Task A_business_that_is_not_waiting_for_payment_cannot_accept(string status)
    {
        var answers = new RecordingSignupAnswerStore();

        var result = await Gate(answers, status).AcceptAsync(Accept(), FromUs);

        Assert.False(result.IsSuccess);
        Assert.Empty(answers.Acceptances);
    }

    [Fact]
    public async Task A_missing_tenant_is_refused()
    {
        var answers = new RecordingSignupAnswerStore();

        var result = await Gate(answers).AcceptAsync(new AcceptTermsRequest(null, SignupStatements.Terms), FromUs);

        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
    }

    [Fact]
    public async Task Accepting_twice_leaves_the_first_row_and_still_answers_accepted()
    {
        var answers = new RecordingSignupAnswerStore { RecordFails = VoiceFailureCodes.Conflict };

        var result = await Gate(answers).AcceptAsync(Accept(), FromUs);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.Accepted);
        Assert.Empty(answers.Acceptances);
    }

    [Fact]
    public async Task A_store_that_cannot_write_is_a_failure_not_an_acceptance()
    {
        var answers = new RecordingSignupAnswerStore { RecordFails = VoiceFailureCodes.StoreUnavailable };

        var result = await Gate(answers).AcceptAsync(Accept(), FromUs);

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.StoreUnavailable, result.ErrorCode);
    }

    private sealed class PendingTenantStore(string status) : IVoiceStore
    {
        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(tenantId == Tenant
                ? Result<VoiceTenant>.Success(new VoiceTenant(Tenant, "Plumbing", "Plumbing, heating, & air-conditioning contractors", DateTimeOffset.UnixEpoch, "US", "FL", "o@example.test", "+15615550100", status, DateTimeOffset.UnixEpoch))
                : Result<VoiceTenant>.Failure("No such tenant.", VoiceFailureCodes.NotFound));

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        // Bring-your-own-storage is not what this test is about; a fake that is asked says there is none.
        public Task<Result<VoiceStorageConnection>> GetStorageConnectionAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Result<VoiceStorageConnection>.Failure("No storage connection.", VoiceFailureCodes.NotFound));

        public Task<Result<VoiceStorageConnection>> SaveStorageConnectionAsync(Guid tenantId, SaveVoiceStorageConnectionCommand command, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectMcpAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectStorageAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
