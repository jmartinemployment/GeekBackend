using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using GeekRepository.Controllers.Voice;
using Microsoft.AspNetCore.Mvc;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// What the two controllers say for each way a store call can end: the status comes from the failure code and never from its
/// wording, a missing body is a 400 and never reaches the store, and a success carries the store's value.
/// </summary>
public sealed class VoiceControllerTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeVoiceStore(Func<Result<VoiceTenant>>? tenant = null, Func<Result<VoiceReceptionistConfig>>? config = null, Func<Result<VoiceMcpConnection>>? mcp = null) : IVoiceStore
    {
        public int Calls { get; private set; }

        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) { Calls++; return Task.FromResult(tenant!()); }
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) { Calls++; return Task.FromResult(config!()); }

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) { Calls++; return Task.FromResult(tenant!()); }
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) { Calls++; return Task.FromResult(tenant!()); }
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public List<VoiceReceptionistConfig> Saved { get; } = new();

        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig c, CancellationToken ct = default) { Calls++; Saved.Add(c); return Task.FromResult(config is not null ? config() : Result<VoiceReceptionistConfig>.Success(c)); }
        // Bring-your-own-storage is not what this test is about; a fake that is asked says there is none.
        public Task<Result<VoiceStorageConnection>> GetStorageConnectionAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Result<VoiceStorageConnection>.Failure("No storage connection.", VoiceFailureCodes.NotFound));

        public Task<Result<VoiceStorageConnection>> SaveStorageConnectionAsync(Guid tenantId, SaveVoiceStorageConnectionCommand command, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) { Calls++; return Task.FromResult(mcp!()); }
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectMcpAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceConnectionRemoved>> DisconnectStorageAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static VoiceTenant TenantWithEmail(string email, string status) => SomeTenant() with { ContactEmail = email, Status = status };

    [Fact]
    public async Task The_exempt_email_is_activated_without_billing_whatever_its_case()
    {
        var store = new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Success(TenantWithEmail(" Owner@GeekAtYourSpot.example ", VoiceTenantStatuses.PendingPayment)));
        var exemption = new RecordingBillingExemption(id => Result<VoiceTenant>.Success(TenantWithEmail("owner@geekatyourspot.example", VoiceTenantStatuses.Active)));

        var result = await new BillingExemption(store, exemption, new BillingExemptionOptions("owner@geekatyourspot.example")).ApplyAsync(new BillingExemptionRequest(Tenant));

        Assert.True(result.IsSuccess);
        Assert.Equal(new BillingExemptionOutcome(true, VoiceTenantStatuses.Active), result.Value);
        Assert.Equal([Tenant], exemption.Activated);
    }

    [Fact]
    public async Task The_exempt_tenant_is_saved_with_the_company_caller_questions()
    {
        var store = new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Success(TenantWithEmail("owner@geekatyourspot.example", VoiceTenantStatuses.PendingPayment)));
        var exemption = new RecordingBillingExemption(id => Result<VoiceTenant>.Success(TenantWithEmail("owner@geekatyourspot.example", VoiceTenantStatuses.Active)));

        var result = await new BillingExemption(store, exemption, new BillingExemptionOptions("owner@geekatyourspot.example")).ApplyAsync(new BillingExemptionRequest(Tenant));

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(store.Saved);
        var script = ReceptionistDefaults.ScriptFor(SomeTenant()).Value!;
        Assert.Equal(script.Greeting, saved.GreetingMessage);
        Assert.Equal(script.Prompt, saved.SystemPromptTemplate);
        Assert.Equal(CompanyCallerQuestions.ToJson().GetRawText(), saved.QualifyingQuestions.GetRawText());
        Assert.Equal(CompanyCallerQuestions.OpeningQuestion, saved.QualifyingQuestions.GetProperty("openingQuestion").GetString());
        Assert.Equal(14, saved.QualifyingQuestions.GetProperty("areas").GetArrayLength());
    }

    [Fact]
    public async Task A_failed_save_of_the_exempt_settings_is_reported_and_nothing_is_activated()
    {
        var store = new FakeVoiceStore(
            tenant: () => Result<VoiceTenant>.Success(TenantWithEmail("owner@geekatyourspot.example", VoiceTenantStatuses.PendingPayment)),
            config: () => Result<VoiceReceptionistConfig>.Failure("The store is unavailable.", VoiceFailureCodes.StoreUnavailable));
        var exemption = new RecordingBillingExemption(id => Result<VoiceTenant>.Success(TenantWithEmail("owner@geekatyourspot.example", VoiceTenantStatuses.Active)));

        var result = await new BillingExemption(store, exemption, new BillingExemptionOptions("owner@geekatyourspot.example")).ApplyAsync(new BillingExemptionRequest(Tenant));

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.StoreUnavailable, result.ErrorCode);
        Assert.Empty(exemption.Activated);
    }

    [Fact]
    public async Task Any_other_email_is_not_exempt_and_nothing_is_activated()
    {
        var store = new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Success(TenantWithEmail("client@example.com", VoiceTenantStatuses.PendingPayment)));
        var exemption = new RecordingBillingExemption();

        var result = await new BillingExemption(store, exemption, new BillingExemptionOptions("owner@geekatyourspot.example")).ApplyAsync(new BillingExemptionRequest(Tenant));

        Assert.Equal(new BillingExemptionOutcome(false, VoiceTenantStatuses.PendingPayment), result.Value);
        Assert.Empty(exemption.Activated);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task With_no_exempt_email_set_nobody_is_exempt()
    {
        var store = new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Success(TenantWithEmail("owner@geekatyourspot.example", VoiceTenantStatuses.PendingPayment)));
        var exemption = new RecordingBillingExemption();

        var result = await new BillingExemption(store, exemption, new BillingExemptionOptions(null)).ApplyAsync(new BillingExemptionRequest(Tenant));

        Assert.False(result.Value!.Exempt);
        Assert.Empty(exemption.Activated);
    }

    [Fact]
    public async Task A_failed_activation_is_reported_not_hidden()
    {
        var store = new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Success(TenantWithEmail("owner@geekatyourspot.example", VoiceTenantStatuses.Active)));
        var exemption = new RecordingBillingExemption(id => Result<VoiceTenant>.Failure("The tenant is not waiting for payment, so it cannot be activated.", VoiceFailureCodes.Conflict));

        var result = await new BillingExemption(store, exemption, new BillingExemptionOptions("owner@geekatyourspot.example")).ApplyAsync(new BillingExemptionRequest(Tenant));

        Assert.False(result.IsSuccess);
        Assert.Equal(VoiceFailureCodes.Conflict, result.ErrorCode);
    }

    [Fact]
    public async Task A_request_without_a_tenant_is_refused()
    {
        var result = await new BillingExemption(new FakeVoiceStore(), new RecordingBillingExemption(), new BillingExemptionOptions("owner@geekatyourspot.example")).ApplyAsync(new BillingExemptionRequest(null));
        Assert.Equal(VoiceFailureCodes.InvalidRequest, result.ErrorCode);
    }

    private static VoiceTenant SomeTenant() => new(Tenant, "Boca Cool HVAC", "Plumbing, heating, & air-conditioning contractors", DateTimeOffset.UnixEpoch, "US", "FL", "a@b.example", "+15615550101", "pending_payment", DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(VoiceFailureCodes.NotFound, 404)]
    [InlineData(VoiceFailureCodes.InvalidRequest, 400)]
    [InlineData(VoiceFailureCodes.Conflict, 409)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, 503)]
    [InlineData("a code nobody has heard of", 503)]
    public async Task Onboarding_status_comes_from_the_failure_code(string code, int status)
    {
        var controller = new VoiceOnboardingController(new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Failure("words that decide nothing", code)), new RecordingSignupAnswerStore(), new RecordingBillingExemption());

        var result = await controller.Get(Tenant, CancellationToken.None);

        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(VoiceFailureCodes.NotFound, 404)]
    [InlineData(VoiceFailureCodes.InvalidRequest, 400)]
    [InlineData(VoiceFailureCodes.Conflict, 409)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, 503)]
    public async Task Tenant_status_comes_from_the_failure_code(string code, int status)
    {
        var controller = new VoiceTenantController(new FakeVoiceStore(config: () => Result<VoiceReceptionistConfig>.Failure("words that decide nothing", code)));

        var result = await controller.GetConfig(Tenant, CancellationToken.None);

        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(VoiceFailureCodes.NotFound, 404)]
    [InlineData(VoiceFailureCodes.InvalidRequest, 400)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, 503)]
    public async Task Saving_a_calendar_connection_takes_its_status_from_the_failure_code(string code, int status)
    {
        var controller = new VoiceTenantController(new FakeVoiceStore(mcp: () => Result<VoiceMcpConnection>.Failure("words that decide nothing", code)));

        var result = await controller.SaveMcpConnection(Tenant, VoiceMcpProviders.GoogleCalendar, new("access", "refresh", null, null), CancellationToken.None);

        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task A_saved_calendar_connection_carries_the_stored_value()
    {
        var saved = new VoiceMcpConnection(Tenant, VoiceMcpProviders.GoogleCalendar, "access", "refresh", null, "primary", true);
        var controller = new VoiceTenantController(new FakeVoiceStore(mcp: () => Result<VoiceMcpConnection>.Success(saved)));

        var result = await controller.SaveMcpConnection(Tenant, VoiceMcpProviders.GoogleCalendar, new("access", "refresh", null, "primary"), CancellationToken.None);

        Assert.Same(saved, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task A_not_found_result_with_no_code_is_a_404()
    {
        var controller = new VoiceOnboardingController(new FakeVoiceStore(tenant: () => Result<VoiceTenant>.NotFound("no such tenant")), new RecordingSignupAnswerStore(), new RecordingBillingExemption());

        var result = await controller.Get(Tenant, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task A_success_carries_the_value()
    {
        var tenant = SomeTenant();
        var controller = new VoiceOnboardingController(new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Success(tenant)), new RecordingSignupAnswerStore(), new RecordingBillingExemption());

        var result = await controller.Get(Tenant, CancellationToken.None);

        Assert.Same(tenant, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task A_missing_body_is_a_400_and_never_reaches_the_store()
    {
        var store = new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Success(SomeTenant()), config: () => throw new InvalidOperationException("must not be called"));
        var onboarding = new VoiceOnboardingController(store, new RecordingSignupAnswerStore(), new RecordingBillingExemption());
        var tenants = new VoiceTenantController(store);

        Assert.IsType<BadRequestObjectResult>(await onboarding.Create(null!, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await onboarding.Activate(Tenant, null!, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await tenants.SaveConfig(Tenant, null!, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await tenants.SaveMcpConnection(Tenant, VoiceMcpProviders.GoogleCalendar, null!, CancellationToken.None));
        Assert.Equal(0, store.Calls);
    }
}
