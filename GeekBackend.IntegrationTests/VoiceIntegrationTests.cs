extern alias GeekRepo;

using System.Text.Json;
using GeekApplication.Models.Voice;
using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// geek_voice against a real Postgres with the vector extension: the script applies, a tenant is created and activated once,
/// one tenant can never read, search, overwrite or reference another tenant's rows, and a retried call report is one row.
/// Runs only with TEST_DATABASE_URL set. Row-level security is not what these prove: the usual login owns the tables or has
/// BYPASSRLS, so the WHERE clauses are the guard under test.
/// </summary>
public sealed class VoiceIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonElement EmptyList = JsonDocument.Parse("[]").RootElement;
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement;

    private static VoiceRepository NewRepository() =>
        new(new NpgsqlConnectionFactory(IntegrationTestConfig.DatabaseUrl!), NullLogger<VoiceRepository>.Instance);

    private static string Unique() => Guid.NewGuid().ToString("N");

    private static float[] Axis(int hot)
    {
        var v = new float[VoiceEmbedding.Dimensions];
        v[hot] = 1f;
        return v;
    }

    private static async Task<Guid> ActiveTenantAsync(VoiceRepository store)
    {
        var created = await store.CreateTenantAsync(new(
            "Boca Cool HVAC " + Unique()[..6], "Plumbing, heating, & air-conditioning contractors", "238220", false, null, null, Now, "US", "FL", "Palm Beach", "dispatch@example.test", "+15615550101"));
        Assert.True(created.IsSuccess, created.Error);
        var id = created.Value!.Id;

        var activated = await store.ActivateTenantAsync(id, new(
            VoiceSubscriptionTiers.Standard, 48m, 250, 0.50m, "I-" + Unique(), "VAULT-" + Unique(), Now, Now.AddMonths(1), "active",
            "AC" + Unique(), "ciphertext", "+1561555" + Random.Shared.Next(1000, 9999), "PN" + Unique(), "agent-1", null, null, "not_started", false));
        Assert.True(activated.IsSuccess, activated.Error);
        return id;
    }

    [Fact]
    public async Task A_tenant_is_created_pending_activated_once_and_then_has_telephony()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();

        var created = await store.CreateTenantAsync(new(
            "Boca Cool HVAC", "Plumbing, heating, & air-conditioning contractors", "238220", false, null, null, Now, "US", "FL", null, "dispatch@example.test", "+15615550101"));
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(VoiceTenantStatuses.PendingPayment, created.Value!.Status);
        var id = created.Value.Id;

        Assert.Equal(VoiceFailureCodes.NotFound, (await store.GetTelephonyAsync(id)).ErrorCode);

        var command = new ActivateVoiceTenantCommand(
            VoiceSubscriptionTiers.Starter, 30m, 90, 0.50m, "I-" + Unique(), "VAULT-" + Unique(), Now, Now.AddMonths(1), "active",
            "AC" + Unique(), "ciphertext", "+1561555" + Random.Shared.Next(1000, 9999), "PN" + Unique(), "agent-1", null, null, "not_started", false);
        var activated = await store.ActivateTenantAsync(id, command);
        Assert.True(activated.IsSuccess, activated.Error);
        Assert.Equal(VoiceTenantStatuses.Active, activated.Value!.Status);

        var telephony = await store.GetTelephonyAsync(id);
        Assert.True(telephony.IsSuccess, telephony.Error);
        Assert.Equal(command.AssignedPhoneNumber, telephony.Value!.AssignedPhoneNumber);

        var again = await store.ActivateTenantAsync(id, command with { PaypalSubscriptionId = "I-" + Unique(), AssignedPhoneNumber = "+1561555" + Random.Shared.Next(1000, 9999) });
        Assert.Equal(VoiceFailureCodes.Conflict, again.ErrorCode);
    }

    [Fact]
    public async Task A_phone_number_already_assigned_to_another_tenant_is_a_conflict_and_leaves_the_tenant_pending()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();
        var first = await ActiveTenantAsync(store);
        var number = (await store.GetTelephonyAsync(first)).Value!.AssignedPhoneNumber;

        var second = await store.CreateTenantAsync(new(
            "Other HVAC", "Plumbing, heating, & air-conditioning contractors", "238220", false, null, null, Now, "US", "FL", null, "other@example.test", "+15615550102"));
        var clash = await store.ActivateTenantAsync(second.Value!.Id, new(
            VoiceSubscriptionTiers.Pro, 148m, 1000, 0.50m, "I-" + Unique(), "VAULT-" + Unique(), Now, Now.AddMonths(1), "active",
            "AC" + Unique(), "ciphertext", number, "PN" + Unique(), null, null, null, "not_started", false));

        Assert.Equal(VoiceFailureCodes.Conflict, clash.ErrorCode);
        Assert.Equal(VoiceTenantStatuses.PendingPayment, (await store.GetTenantAsync(second.Value.Id)).Value!.Status);
    }

    [Fact]
    public async Task Settings_save_as_a_whole_and_one_tenant_cannot_read_anothers()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();
        var tenantA = await ActiveTenantAsync(store);
        var tenantB = await ActiveTenantAsync(store);

        var config = new VoiceReceptionistConfig(
            "Thanks for calling Boca Cool.", "voice-1", "You answer for {business}.", EmptyObject, EmptyList,
            VoiceTransferModes.WarmTransfer, "+15615550102", VoiceTransferCallerIdModes.OriginalCaller, ["flood", "no ac"], true);
        var saved = await store.SaveConfigAsync(tenantA, config);
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal(["flood", "no ac"], saved.Value!.EmergencyKeywords);

        var changed = await store.SaveConfigAsync(tenantA, config with { TransferMode = VoiceTransferModes.VoicemailOnly, TransferTargetNumber = null });
        Assert.Equal(VoiceTransferModes.VoicemailOnly, changed.Value!.TransferMode);

        Assert.Equal(VoiceFailureCodes.NotFound, (await store.GetConfigAsync(tenantB)).ErrorCode);
        Assert.Equal(VoiceFailureCodes.NotFound, (await store.GetMcpConnectionAsync(tenantB, VoiceMcpProviders.GoogleCalendar)).ErrorCode);
    }

    [Fact]
    public async Task A_calendar_connection_saves_replaces_and_stays_inside_its_tenant()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();
        var tenantA = await ActiveTenantAsync(store);
        var tenantB = await ActiveTenantAsync(store);

        var saved = await store.SaveMcpConnectionAsync(tenantA, VoiceMcpProviders.GoogleCalendar, new("access-1", "refresh-1", Now.AddHours(1), "primary"));
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal("access-1", (await store.GetMcpConnectionAsync(tenantA, VoiceMcpProviders.GoogleCalendar)).Value!.OauthAccessTokenEncrypted);

        var replaced = await store.SaveMcpConnectionAsync(tenantA, VoiceMcpProviders.GoogleCalendar, new("access-2", "refresh-2", null, null));
        Assert.Equal("refresh-2", replaced.Value!.OauthRefreshTokenEncrypted);
        Assert.Null(replaced.Value.ExternalCalendarId);
        Assert.True(replaced.Value.IsActive);

        Assert.Equal(VoiceFailureCodes.NotFound, (await store.GetMcpConnectionAsync(tenantB, VoiceMcpProviders.GoogleCalendar)).ErrorCode);
        Assert.Equal(VoiceFailureCodes.NotFound, (await store.SaveMcpConnectionAsync(Guid.NewGuid(), VoiceMcpProviders.Outlook, new("a", "r", null, null))).ErrorCode);
    }

    [Fact]
    public async Task Knowledge_search_returns_the_closest_chunk_first_and_only_from_its_own_tenant()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();
        var tenantA = await ActiveTenantAsync(store);
        var tenantB = await ActiveTenantAsync(store);

        var document = await store.CreateKnowledgeDocumentAsync(tenantA, new(
            VoiceKnowledgeSourceTypes.GuidedTradeForm, "Hours and prices", null, "We open at eight. A visit costs $89.",
            [new(0, "We open at eight.", Axis(0)), new(1, "A visit costs $89.", Axis(1))]));
        Assert.True(document.IsSuccess, document.Error);

        var hits = await store.SearchKnowledgeAsync(tenantA, new(Axis(1), 2));
        Assert.True(hits.IsSuccess, hits.Error);
        Assert.Equal(2, hits.Value!.Count);
        Assert.Equal("A visit costs $89.", hits.Value[0].ChunkText);
        Assert.True(hits.Value[0].Similarity > hits.Value[1].Similarity);

        var other = await store.SearchKnowledgeAsync(tenantB, new(Axis(1), 2));
        Assert.True(other.IsSuccess, other.Error);
        Assert.Empty(other.Value!);
    }

    [Fact]
    public async Task A_retried_call_report_is_one_row_that_fills_in_and_never_loses_a_summary()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();
        var tenant = await ActiveTenantAsync(store);
        var callId = "call_" + Unique();

        var first = await store.UpsertCallAsync(tenant, new(callId, null, "+15615550111", "in_progress", Now, null, 0, 0, EmptyList, null, null, null));
        var second = await store.UpsertCallAsync(tenant, new(callId, null, "+15615550111", "completed", Now, Now.AddMinutes(3), 180, 3, EmptyList, "Needs a new capacitor.", "booked", null));
        var third = await store.UpsertCallAsync(tenant, new(callId, null, "+15615550111", "completed", Now, Now.AddMinutes(3), 120, 2, EmptyList, null, null, null));

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(first.Value!.CallId, second.Value!.CallId);
        Assert.Equal(first.Value.CallId, third.Value!.CallId);
    }

    [Fact]
    public async Task Another_tenant_can_neither_overwrite_a_call_nor_attach_an_appointment_to_it()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();
        var tenantA = await ActiveTenantAsync(store);
        var tenantB = await ActiveTenantAsync(store);
        var callId = "call_" + Unique();

        var call = await store.UpsertCallAsync(tenantA, new(callId, null, "+15615550111", "completed", Now, Now.AddMinutes(3), 180, 3, EmptyList, null, null, null));
        Assert.True(call.IsSuccess, call.Error);

        var overwrite = await store.UpsertCallAsync(tenantB, new(callId, null, "+15615550999", "completed", Now, Now.AddMinutes(1), 60, 1, EmptyList, null, null, null));
        Assert.Equal(VoiceFailureCodes.Conflict, overwrite.ErrorCode);

        var appointment = new CreateVoiceAppointmentCommand(
            call.Value!.CallId, "Pat Jones", "+15615550112", null, "12 Ocean Ave", "No cooling", Now.AddDays(1), Now.AddDays(1).AddHours(1), null, VoiceMcpProviders.GoogleCalendar);
        var crossed = await store.CreateAppointmentAsync(tenantB, appointment);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, crossed.ErrorCode);

        var own = await store.CreateAppointmentAsync(tenantA, appointment);
        Assert.True(own.IsSuccess, own.Error);
    }
}
