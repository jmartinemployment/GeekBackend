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

    private sealed class FakeVoiceStore(Func<Result<VoiceTenant>>? tenant = null, Func<Result<VoiceReceptionistConfig>>? config = null) : IVoiceStore
    {
        public int Calls { get; private set; }

        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) { Calls++; return Task.FromResult(tenant!()); }
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) { Calls++; return Task.FromResult(config!()); }

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) { Calls++; return Task.FromResult(tenant!()); }
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) { Calls++; return Task.FromResult(tenant!()); }
        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig c, CancellationToken ct = default) { Calls++; return Task.FromResult(config!()); }
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static VoiceTenant SomeTenant() => new(Tenant, "Boca Cool HVAC", "HVAC", false, null, null, DateTimeOffset.UnixEpoch, "US", "FL", null, "a@b.example", "+15615550101", "pending_payment", DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(VoiceFailureCodes.NotFound, 404)]
    [InlineData(VoiceFailureCodes.InvalidRequest, 400)]
    [InlineData(VoiceFailureCodes.Conflict, 409)]
    [InlineData(VoiceFailureCodes.StoreUnavailable, 503)]
    [InlineData("a code nobody has heard of", 503)]
    public async Task Onboarding_status_comes_from_the_failure_code(string code, int status)
    {
        var controller = new VoiceOnboardingController(new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Failure("words that decide nothing", code)));

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

    [Fact]
    public async Task A_not_found_result_with_no_code_is_a_404()
    {
        var controller = new VoiceOnboardingController(new FakeVoiceStore(tenant: () => Result<VoiceTenant>.NotFound("no such tenant")));

        var result = await controller.Get(Tenant, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task A_success_carries_the_value()
    {
        var tenant = SomeTenant();
        var controller = new VoiceOnboardingController(new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Success(tenant)));

        var result = await controller.Get(Tenant, CancellationToken.None);

        Assert.Same(tenant, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task A_missing_body_is_a_400_and_never_reaches_the_store()
    {
        var store = new FakeVoiceStore(tenant: () => Result<VoiceTenant>.Success(SomeTenant()), config: () => throw new InvalidOperationException("must not be called"));
        var onboarding = new VoiceOnboardingController(store);
        var tenants = new VoiceTenantController(store);

        Assert.IsType<BadRequestObjectResult>(await onboarding.Create(null!, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await onboarding.Activate(Tenant, null!, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await tenants.SaveConfig(Tenant, null!, CancellationToken.None));
        Assert.Equal(0, store.Calls);
    }
}
