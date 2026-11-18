using System.Security.Cryptography;
using System.Text;
using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Receptionist;
using GeekAPI.Services.Voice;
using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Calls;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// A tenant's call routes. Only a post signed with the tenant's own subaccount auth token and naming that subaccount is read. A call
/// hears the company line's closing message with the tenant's business name and is hung up, and the tenant's standing is read on every
/// call. A finished call is recorded from Twilio's status callback by its call SID, its minutes rounded up per call, with no caller
/// number; a completed call without a length, or a post missing its SID or status, is refused rather than recorded as zero minutes.
/// </summary>
public sealed class TenantCallTests
{
    private static readonly Guid Tenant = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private const string KeyBase64 = "BwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwc=";
    private const string BaseUrl = "https://api.example.test";
    private const string SubSid = "ACsub1";
    private const string SubToken = "sub-token-secret";
    private const string Business = "Boca Cool HVAC";

    private static readonly VoiceCredentialProtector Protector = new(() => KeyBase64);

    private sealed class FakeVoice : IVoiceStore
    {
        public bool NoTelephony { get; init; }
        public List<UpsertVoiceCallCommand> Calls { get; } = new();

        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(NoTelephony
            ? Result<VoiceTelephony>.Failure("The tenant has no telephony.", VoiceFailureCodes.NotFound)
            : Result<VoiceTelephony>.Success(new VoiceTelephony(tenantId, SubSid, Protector.Protect(SubToken, VoiceSecretPurposes.TwilioAuthToken).Value!, "+15615550100", "PN1", null, null, "not_requested", false)));

        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(Result<VoiceTenant>.Success(new VoiceTenant(
            tenantId, Business, "HVAC", false, null, null, Now, "US", "FL", "owner@bocacool.example", "+15615550101", VoiceTenantStatuses.Active, Now)));

        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default)
        {
            Calls.Add(command);
            return Task.FromResult(Result<VoiceCallStored>.Success(new VoiceCallStored(Guid.NewGuid())));
        }

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeBilling(VoiceBillingStanding standing) : IVoiceBillingStore
    {
        public int StandingReads { get; private set; }

        public Task<Result<VoiceBillingStanding>> GetStandingAsync(Guid tenantId, CancellationToken ct = default)
        {
            StandingReads++;
            return Task.FromResult(Result<VoiceBillingStanding>.Success(standing));
        }

        public Task<Result<IReadOnlyList<VoiceSubscriptionDue>>> ListDueAsync(ListVoiceSubscriptionsDueCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceBillingPeriodClosed>> CloseBillingPeriodAsync(Guid tenantId, CloseVoiceBillingPeriodCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceOverageCharge>> SettleOverageChargeAsync(Guid tenantId, Guid chargeId, SettleVoiceOverageChargeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceBillingPeriod>> AdvanceBillingPeriodAsync(Guid tenantId, AdvanceVoiceBillingPeriodCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetBillingStatusAsync(Guid tenantId, SetVoiceBillingStatusCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceSuspendedTenant>>> ListSuspendedAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceBillingStanding>> SuspendTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceBillingStanding>> RestoreTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceBillingStanding>> MarkAnsweringStoppedAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceBillingStanding>> CancelTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> MarkTelephonyReleasedAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceOverageCharge>> CollectOverageChargeAsync(Guid tenantId, Guid chargeId, CollectVoiceOverageChargeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static VoiceBillingStanding Standing(string status, DateTimeOffset? suspendedAt = null, DateTimeOffset? stoppedAt = null) => new(
        Tenant, status, suspendedAt, stoppedAt, Guid.NewGuid(), VoiceSubscriptionTiers.Starter, VoiceBillingStatuses.Active, "I-SUB",
        Now.AddDays(-10), Now.AddDays(20), []);

    private static Dictionary<string, string> Completed(string duration = "61", string accountSid = SubSid) => new(StringComparer.Ordinal)
    {
        ["AccountSid"] = accountSid,
        ["CallSid"] = "CA5d0d",
        ["CallStatus"] = "completed",
        ["CallDuration"] = duration,
        ["From"] = "+15615550111",
        ["To"] = "+15615550100",
        ["Direction"] = "inbound",
        ["Timestamp"] = "Wed, 18 Nov 2026 14:58:00 +0000",
    };

    /// <summary>Twilio's signature: HMAC-SHA1 with the auth token over the full URL followed by each parameter's name and value, sorted by name.</summary>
    private static string Sign(string url, IReadOnlyDictionary<string, string> form, string token)
    {
        var data = new StringBuilder(url);
        foreach (var kv in form.OrderBy(kv => kv.Key, StringComparer.Ordinal)) data.Append(kv.Key).Append(kv.Value);
        return Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(data.ToString())));
    }

    private static TenantTwilioWebhook Webhook(FakeVoice store, string? baseUrl = BaseUrl) =>
        new(store, Protector, new TwilioOptions("ACmaster", "master-token", baseUrl), NullLogger<TenantTwilioWebhook>.Instance);

    // ----- Minutes -----

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(1L, 1)]
    [InlineData(60L, 1)]
    [InlineData(61L, 2)]
    [InlineData(3599L, 60)]
    public void Every_started_minute_is_billable(long seconds, int minutes) =>
        Assert.Equal(minutes, TwilioCallStatusRecorder.BillableMinutes(seconds));

    private static async Task<(Result<TwilioCallRecorded> Result, FakeVoice Store)> Record(Dictionary<string, string> form)
    {
        var store = new FakeVoice();
        return (await new TwilioCallStatusRecorder(store, NullLogger<TwilioCallStatusRecorder>.Instance).RecordAsync(Tenant, form, Now), store);
    }

    [Fact]
    public async Task A_completed_call_is_recorded_by_its_twilio_sid_with_its_minutes_rounded_up_and_no_caller_number()
    {
        var (result, store) = await Record(Completed("61"));
        Assert.True(result.IsSuccess, result.Error);

        var call = Assert.Single(store.Calls);
        Assert.Equal("CA5d0d", call.TwilioCallSid);
        Assert.Equal("completed", call.CallStatus);
        Assert.Equal(61, call.DurationSeconds);
        Assert.Equal(2, call.BillableMinutes);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 14, 58, 0, TimeSpan.Zero), call.EndedAt);
        Assert.Equal(call.EndedAt!.Value.AddSeconds(-61), call.StartedAt);
        Assert.DoesNotContain(typeof(UpsertVoiceCallCommand).GetProperties(), p => p.Name is "CallerNumber" or "From" or "Transcript" or "RecordingUrl");
    }

    [Fact]
    public async Task Without_a_timestamp_the_call_ends_when_the_post_arrived()
    {
        var form = Completed("30");
        form.Remove("Timestamp");
        var (_, store) = await Record(form);
        var call = Assert.Single(store.Calls);
        Assert.Equal(Now, call.EndedAt);
        Assert.Equal(1, call.BillableMinutes);
    }

    [Theory]
    [InlineData("CallSid")]
    [InlineData("CallStatus")]
    [InlineData("CallDuration")]
    public async Task A_completed_call_missing_a_required_field_is_refused_not_recorded(string missing)
    {
        var form = Completed();
        form.Remove(missing);
        var (result, store) = await Record(form);
        Assert.Equal(TenantCallFailureCodes.InvalidCall, result.ErrorCode);
        Assert.Empty(store.Calls);
    }

    [Theory]
    [InlineData("CallDuration", "a minute")]
    [InlineData("CallDuration", "-5")]
    [InlineData("Timestamp", "yesterday-ish")]
    public async Task A_length_or_time_that_does_not_parse_is_refused(string field, string value)
    {
        var form = Completed();
        form[field] = value;
        var (result, store) = await Record(form);
        Assert.Equal(TenantCallFailureCodes.InvalidCall, result.ErrorCode);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_call_nobody_answered_is_recorded_with_no_minutes()
    {
        var form = Completed();
        form["CallStatus"] = "no-answer";
        form.Remove("CallDuration");
        var (result, store) = await Record(form);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, Assert.Single(store.Calls).BillableMinutes);
    }

    [Fact]
    public async Task A_status_a_call_does_not_end_in_is_accepted_and_not_recorded()
    {
        var form = Completed();
        form["CallStatus"] = "in-progress";
        var (result, store) = await Record(form);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CallId);
        Assert.Empty(store.Calls);
    }

    // ----- Signature and account -----

    private static string StatusUrl => BaseUrl + TwilioProvisioner.TenantCallStatusPath(Tenant);

    [Fact]
    public async Task A_post_signed_with_the_tenants_subaccount_token_is_accepted()
    {
        var form = Completed();
        var result = await Webhook(new FakeVoice()).VerifyAsync(Tenant, TwilioProvisioner.TenantCallStatusPath(Tenant), Sign(StatusUrl, form, SubToken), form);
        Assert.True(result.IsSuccess, result.Error);
    }

    [Theory]
    [InlineData("master-token")]
    [InlineData("another-tenants-token")]
    public async Task A_post_signed_with_another_token_is_refused(string token)
    {
        var form = Completed();
        var result = await Webhook(new FakeVoice()).VerifyAsync(Tenant, TwilioProvisioner.TenantCallStatusPath(Tenant), Sign(StatusUrl, form, token), form);
        Assert.Equal(TenantCallFailureCodes.SignatureInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task A_post_whose_fields_changed_after_signing_or_with_no_signature_is_refused()
    {
        var form = Completed("61");
        var signature = Sign(StatusUrl, form, SubToken);
        form["CallDuration"] = "6100";
        Assert.Equal(TenantCallFailureCodes.SignatureInvalid, (await Webhook(new FakeVoice()).VerifyAsync(Tenant, TwilioProvisioner.TenantCallStatusPath(Tenant), signature, form)).ErrorCode);
        Assert.Equal(TenantCallFailureCodes.SignatureInvalid, (await Webhook(new FakeVoice()).VerifyAsync(Tenant, TwilioProvisioner.TenantCallStatusPath(Tenant), null, form)).ErrorCode);
    }

    [Fact]
    public async Task A_signed_post_naming_another_account_is_refused()
    {
        var form = Completed(accountSid: "ACsomeoneelse");
        var result = await Webhook(new FakeVoice()).VerifyAsync(Tenant, TwilioProvisioner.TenantCallStatusPath(Tenant), Sign(StatusUrl, form, SubToken), form);
        Assert.Equal(TenantCallFailureCodes.AccountMismatch, result.ErrorCode);
    }

    [Fact]
    public async Task Without_the_public_address_nothing_is_accepted()
    {
        var form = Completed();
        var result = await Webhook(new FakeVoice(), baseUrl: null).VerifyAsync(Tenant, TwilioProvisioner.TenantCallStatusPath(Tenant), Sign(StatusUrl, form, SubToken), form);
        Assert.Equal(TelephonyFailureCodes.TelephonyNotConfigured, result.ErrorCode);
    }

    [Fact]
    public async Task A_tenant_with_no_number_is_not_found()
    {
        var form = Completed();
        var result = await Webhook(new FakeVoice { NoTelephony = true }).VerifyAsync(Tenant, TwilioProvisioner.TenantCallStatusPath(Tenant), Sign(StatusUrl, form, SubToken), form);
        Assert.Equal(VoiceFailureCodes.NotFound, result.ErrorCode);
    }

    // ----- Standing -----

    [Fact]
    public void Only_an_active_tenant_or_one_inside_its_grace_may_be_answered()
    {
        Assert.True(TenantStanding.MayAnswer(Standing(VoiceTenantStatuses.Active), Now));
        Assert.True(TenantStanding.MayAnswer(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now.AddDays(-6)), Now));
        Assert.False(TenantStanding.MayAnswer(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now.AddDays(-7)), Now));
        Assert.False(TenantStanding.MayAnswer(Standing(VoiceTenantStatuses.Suspended, suspendedAt: Now.AddDays(-1), stoppedAt: Now), Now));
        Assert.False(TenantStanding.MayAnswer(Standing(VoiceTenantStatuses.Cancelled), Now));
        Assert.False(TenantStanding.MayAnswer(Standing(VoiceTenantStatuses.PendingPayment), Now));
    }

    // ----- Routes -----

    private static async Task<(int Status, string Body, FakeVoice Store, FakeBilling Billing)> Post(string route, Dictionary<string, string> form, string? token = SubToken, VoiceBillingStanding? standing = null)
    {
        var store = new FakeVoice();
        var billing = new FakeBilling(standing ?? Standing(VoiceTenantStatuses.Active));
        var controller = new TenantCallController(
            Webhook(store),
            new TwilioCallStatusRecorder(store, NullLogger<TwilioCallStatusRecorder>.Instance),
            store,
            billing,
            new FakeTimeProvider(Now),
            NullLogger<TenantCallController>.Instance);

        var path = $"/{TenantCallController.RoutePrefix}/{Tenant}/{route}";
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('&', form.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)))));
        if (token is not null) context.Request.Headers[TenantTwilioWebhook.SignatureHeader] = Sign(BaseUrl + path, form, token);
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        IActionResult result = route == "voice" ? await controller.Voice(Tenant, CancellationToken.None) : await controller.CallStatus(Tenant, CancellationToken.None);
        return result switch
        {
            ContentResult c => (c.StatusCode ?? 200, c.Content ?? "", store, billing),
            StatusCodeResult s => (s.StatusCode, "", store, billing),
            ObjectResult o => (o.StatusCode ?? 200, "", store, billing),
            _ => (0, "", store, billing),
        };
    }

    private static Dictionary<string, string> Ringing() => new(StringComparer.Ordinal)
    {
        ["AccountSid"] = SubSid,
        ["CallSid"] = "CA5d0d",
        ["CallStatus"] = "ringing",
        ["From"] = "+15615550111",
        ["To"] = "+15615550100",
        ["Direction"] = "inbound",
    };

    [Fact]
    public async Task A_call_hears_the_closing_message_with_the_business_name_and_is_hung_up()
    {
        var (status, body, _, billing) = await Post("voice", Ringing());

        Assert.Equal(200, status);
        Assert.Contains(ReceptionistEndpoints.UnansweredText(Business), body);
        Assert.Contains("<Hangup", body);
        Assert.DoesNotContain("<Record", body);
        Assert.DoesNotContain("<Dial", body);
        Assert.Equal(1, billing.StandingReads);
    }

    [Theory]
    [InlineData(VoiceTenantStatuses.Cancelled)]
    [InlineData(VoiceTenantStatuses.Suspended)]
    public async Task A_tenant_that_may_not_be_answered_has_its_standing_read_and_hears_the_closing_message(string tenantStatus)
    {
        var standing = Standing(tenantStatus, suspendedAt: tenantStatus == VoiceTenantStatuses.Suspended ? Now.AddDays(-8) : null);
        var (status, body, _, billing) = await Post("voice", Ringing(), standing: standing);

        Assert.Equal(200, status);
        Assert.Contains(ReceptionistEndpoints.UnansweredText(Business), body);
        Assert.Equal(1, billing.StandingReads);
    }

    [Fact]
    public async Task An_unsigned_call_is_refused_403_before_the_standing_is_read()
    {
        var (status, _, _, billing) = await Post("voice", Ringing(), token: "wrong-token");
        Assert.Equal(403, status);
        Assert.Equal(0, billing.StandingReads);
    }

    [Fact]
    public async Task The_status_route_records_a_signed_completed_call_and_answers_204()
    {
        var (status, _, store, _) = await Post("call-status", Completed("61"));
        Assert.Equal(204, status);
        Assert.Equal(2, Assert.Single(store.Calls).BillableMinutes);
    }

    [Fact]
    public async Task The_status_route_refuses_a_bad_signature_403_another_account_403_and_a_missing_length_400()
    {
        Assert.Equal(403, (await Post("call-status", Completed(), token: "wrong-token")).Status);
        Assert.Equal(403, (await Post("call-status", Completed(accountSid: "ACsomeoneelse"))).Status);
        var form = Completed();
        form.Remove("CallDuration");
        var (status, _, store, _) = await Post("call-status", form);
        Assert.Equal(400, status);
        Assert.Empty(store.Calls);
    }
}
