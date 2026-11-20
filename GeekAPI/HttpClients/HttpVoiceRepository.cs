using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;

namespace GeekAPI.HttpClients;

/// <summary>
/// /internal/v1/* over the GeekRepository client that sends X-Internal-Api-Key, the <see cref="IVoiceStore"/> GeekAPI's
/// Geek-Receptionist services call (docs/Geek-Receptionist.md section 6). A status code is read and answered as a Result
/// carrying a <see cref="VoiceFailureCodes"/> value, read by <see cref="VoiceRepositoryTransport"/> as every Geek-Receptionist client reads
/// it; nothing here turns a failure into an empty list, so a store that is down is never read as "nothing there".
/// </summary>
public sealed class HttpVoiceRepository : IVoiceStore, IVoiceSignupAnswerStore, IVoiceBillingExemption
{
    private readonly VoiceRepositoryTransport _transport;

    public HttpVoiceRepository(HttpClient http, ILogger<HttpVoiceRepository> logger)
    {
        _transport = new VoiceRepositoryTransport(http, logger);
    }

    // ----- Tenants -----

    public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceTenant>(HttpMethod.Post, "internal/v1/onboarding/tenants", command, ct);

    public Task<Result<VoiceIndustryAnswersRecorded>> RecordIndustryRefusalAsync(VoiceIndustryAnswerSet answers, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceIndustryAnswersRecorded>(HttpMethod.Post, "internal/v1/onboarding/industry-refusals", answers, ct);

    public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceTenant>(HttpMethod.Get, $"internal/v1/onboarding/tenants/{tenantId}", null, ct);

    public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceTenant>(HttpMethod.Post, $"internal/v1/onboarding/tenants/{tenantId}/activate", command, ct);

    public Task<Result<VoiceTenant>> ActivateWithoutBillingAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceTenant>(HttpMethod.Post, $"internal/v1/onboarding/tenants/{tenantId}/activate-without-billing", null, ct);

    // ----- Telephony, configuration, calendar connections -----

    public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceTelephony>(HttpMethod.Get, $"internal/v1/tenants/{tenantId}/telephony", null, ct);

    public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceReceptionistConfig>(HttpMethod.Get, $"internal/v1/tenants/{tenantId}/config", null, ct);

    public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceReceptionistConfig>(HttpMethod.Put, $"internal/v1/tenants/{tenantId}/config", config, ct);

    public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceMcpConnection>(HttpMethod.Get, $"internal/v1/tenants/{tenantId}/mcp/{Uri.EscapeDataString(provider)}", null, ct);

    public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceMcpConnection>(HttpMethod.Put, $"internal/v1/tenants/{tenantId}/mcp/{Uri.EscapeDataString(provider)}", command, ct);

    public Task<Result<VoiceStorageConnection>> GetStorageConnectionAsync(Guid tenantId, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceStorageConnection>(HttpMethod.Get, $"internal/v1/tenants/{tenantId}/storage", null, ct);

    public Task<Result<VoiceStorageConnection>> SaveStorageConnectionAsync(Guid tenantId, SaveVoiceStorageConnectionCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceStorageConnection>(HttpMethod.Put, $"internal/v1/tenants/{tenantId}/storage", command, ct);

    // ----- Calls and appointments -----

    public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceCallStored>(HttpMethod.Post, $"internal/v1/tenants/{tenantId}/calls", command, ct);

    public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) =>
        _transport.SendAsync<VoiceAppointmentCreated>(HttpMethod.Post, $"internal/v1/tenants/{tenantId}/appointments", command, ct);
}
