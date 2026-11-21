extern alias GeekRepo;

using System.Text.Json;
using GeekApplication.Models.Voice;
using GeekRepo::GeekRepository.Infrastructure;
using GeekRepo::GeekRepository.Repositories.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.IntegrationTests;

/// <summary>
/// geek_voice against a real Postgres: the script applies, a tenant is created and activated once, one tenant can never read,
/// overwrite, withdraw or reference another tenant's rows, and a retried call report is one row.
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

    private static async Task<Guid> ActiveTenantAsync(VoiceRepository store)
    {
        var created = await store.CreateTenantAsync(new(
            "Boca Cool HVAC " + Unique()[..6], "Plumbing, heating, & air-conditioning contractors", Now, "US", "FL", "dispatch@example.test", "+15615550101") { IndustryAnswers = IndustryAnswersFixture.AllNo(Now), Attestation = AttestationFixture.Good(Now) });
        Assert.True(created.IsSuccess, created.Error);
        var id = created.Value!.Id;

        var activated = await store.ActivateTenantAsync(id, new(
            VoiceSubscriptionTiers.Standard, 48m, 250, 0.50m, "I-" + Unique(), "VAULT-" + Unique(), Now, Now.AddMonths(1), "active",
            "AC" + Unique(), "ciphertext", "+1561555" + Random.Shared.Next(1000, 9999), "PN" + Unique(), null, null, "not_started", false));
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
            "Boca Cool HVAC", "Plumbing, heating, & air-conditioning contractors", Now, "US", "FL", "dispatch@example.test", "+15615550101") { IndustryAnswers = IndustryAnswersFixture.AllNo(Now), Attestation = AttestationFixture.Good(Now) });
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(VoiceTenantStatuses.PendingPayment, created.Value!.Status);
        var id = created.Value.Id;

        Assert.Equal(VoiceFailureCodes.NotFound, (await store.GetTelephonyAsync(id)).ErrorCode);

        var command = new ActivateVoiceTenantCommand(
            VoiceSubscriptionTiers.Starter, 30m, 90, 0.50m, "I-" + Unique(), "VAULT-" + Unique(), Now, Now.AddMonths(1), "active",
            "AC" + Unique(), "ciphertext", "+1561555" + Random.Shared.Next(1000, 9999), "PN" + Unique(), null, null, "not_started", false);
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
            "Other HVAC", "Plumbing, heating, & air-conditioning contractors", Now, "US", "FL", "other@example.test", "+15615550102") { IndustryAnswers = IndustryAnswersFixture.AllNo(Now), Attestation = AttestationFixture.Good(Now) });
        var clash = await store.ActivateTenantAsync(second.Value!.Id, new(
            VoiceSubscriptionTiers.Pro, 148m, 1000, 0.50m, "I-" + Unique(), "VAULT-" + Unique(), Now, Now.AddMonths(1), "active",
            "AC" + Unique(), "ciphertext", number, "PN" + Unique(), null, null, "not_started", false));

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
            "Thanks for calling Boca Cool.", "You answer for {business}.", EmptyObject, EmptyList,
            VoiceTransferModes.WarmTransfer, "+15615550102", VoiceTransferCallerIdModes.OriginalCaller, ["flood", "no ac"], true);
        var saved = await store.SaveConfigAsync(tenantA, config);
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal(["flood", "no ac"], saved.Value!.EmergencyKeywords);

        var changed = await store.SaveConfigAsync(tenantA, config with { TransferMode = VoiceTransferModes.VoicemailOnly, TransferTargetNumberEncrypted = null });
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

    /// <summary>
    /// Withdrawing a connection takes the row with it. The first withdrawal reports that one went, a second reports that there was
    /// none and is still a success, the tenant's own calendar is then gone, and the other tenant's identical connection is untouched:
    /// one owner's withdrawal can never reach another owner's credentials.
    /// </summary>
    [Fact]
    public async Task A_withdrawn_connection_is_deleted_once_withdrawn_again_reports_nothing_to_remove_and_the_other_tenant_keeps_its_own()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();
        var tenantA = await ActiveTenantAsync(store);
        var tenantB = await ActiveTenantAsync(store);

        Assert.True((await store.SaveMcpConnectionAsync(tenantA, VoiceMcpProviders.GoogleCalendar, new("access-a", "refresh-a", Now.AddHours(1), "primary"))).IsSuccess);
        Assert.True((await store.SaveMcpConnectionAsync(tenantB, VoiceMcpProviders.GoogleCalendar, new("access-b", "refresh-b", Now.AddHours(1), "primary"))).IsSuccess);

        var removed = await store.DisconnectMcpAsync(tenantA, VoiceMcpProviders.GoogleCalendar);
        Assert.True(removed.IsSuccess, removed.Error);
        Assert.True(removed.Value!.Removed);

        var again = await store.DisconnectMcpAsync(tenantA, VoiceMcpProviders.GoogleCalendar);
        Assert.True(again.IsSuccess, again.Error);
        Assert.False(again.Value!.Removed);

        Assert.Equal(VoiceFailureCodes.NotFound, (await store.GetMcpConnectionAsync(tenantA, VoiceMcpProviders.GoogleCalendar)).ErrorCode);
        Assert.Equal("access-b", (await store.GetMcpConnectionAsync(tenantB, VoiceMcpProviders.GoogleCalendar)).Value!.OauthAccessTokenEncrypted);

        // The connection is gone, not deactivated: no row of that tenant and provider is left holding the tokens.
        await using var conn = new Npgsql.NpgsqlConnection(IntegrationTestConfig.DatabaseUrl!);
        await conn.OpenAsync();
        var rows = await Dapper.SqlMapper.ExecuteScalarAsync<long>(conn,
            "SELECT COUNT(*) FROM geek_voice.tenant_mcp_connections WHERE tenant_id = @tenantA AND provider = @provider;",
            new { tenantA, provider = VoiceMcpProviders.GoogleCalendar });
        Assert.Equal(0, rows);
    }

    /// <summary>
    /// The same of a business's own bucket: withdrawing deletes the row and the encrypted keys in it, withdrawing again is a success
    /// that reports nothing to remove, and the other tenant's bucket stays exactly where it was.
    /// </summary>
    [Fact]
    public async Task A_withdrawn_bucket_is_deleted_and_the_other_tenants_bucket_is_untouched()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();
        var tenantA = await ActiveTenantAsync(store);
        var tenantB = await ActiveTenantAsync(store);

        Assert.True((await store.SaveStorageConnectionAsync(tenantA, new(
            VoiceStorageProviders.AwsS3, "key-a", "secret-a", "bucket-a", "calls/", "us-east-1"))).IsSuccess);
        Assert.True((await store.SaveStorageConnectionAsync(tenantB, new(
            VoiceStorageProviders.GoogleCloudStorage, "key-b", "secret-b", "bucket-b", "", "us-central1"))).IsSuccess);

        var removed = await store.DisconnectStorageAsync(tenantA);
        Assert.True(removed.IsSuccess, removed.Error);
        Assert.True(removed.Value!.Removed);

        var again = await store.DisconnectStorageAsync(tenantA);
        Assert.True(again.IsSuccess, again.Error);
        Assert.False(again.Value!.Removed);

        Assert.Equal(VoiceFailureCodes.NotFound, (await store.GetStorageConnectionAsync(tenantA)).ErrorCode);
        Assert.Equal("bucket-b", (await store.GetStorageConnectionAsync(tenantB)).Value!.Bucket);
    }

    [Fact]
    public async Task A_retried_call_report_is_one_row_that_fills_in()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();
        var tenant = await ActiveTenantAsync(store);
        var callId = "CA" + Unique();

        var first = await store.UpsertCallAsync(tenant, new(callId, "in_progress", Now, null, 0, 0, null));
        var second = await store.UpsertCallAsync(tenant, new(callId, "completed", Now, Now.AddMinutes(3), 180, 3, "booked"));
        var third = await store.UpsertCallAsync(tenant, new(callId, "completed", Now, Now.AddMinutes(3), 120, 2, null));

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
        var callId = "CA" + Unique();

        var call = await store.UpsertCallAsync(tenantA, new(callId, "completed", Now, Now.AddMinutes(3), 180, 3, null));
        Assert.True(call.IsSuccess, call.Error);

        var overwrite = await store.UpsertCallAsync(tenantB, new(callId, "completed", Now, Now.AddMinutes(1), 60, 1, null));
        Assert.Equal(VoiceFailureCodes.Conflict, overwrite.ErrorCode);

        var appointment = new CreateVoiceAppointmentCommand(
            call.Value!.CallId, Now.AddDays(1), Now.AddDays(1).AddHours(1), null, VoiceMcpProviders.GoogleCalendar);
        var crossed = await store.CreateAppointmentAsync(tenantB, appointment);
        Assert.Equal(VoiceFailureCodes.InvalidRequest, crossed.ErrorCode);

        var own = await store.CreateAppointmentAsync(tenantA, appointment);
        Assert.True(own.IsSuccess, own.Error);
    }

    [Fact]
    public async Task The_excluded_industry_answers_are_stored_with_the_tenant_and_the_database_refuses_to_change_or_delete_them()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();

        var created = await store.CreateTenantAsync(new(
            "Boca Cool HVAC " + Unique()[..6], "Heating and air conditioning", Now, "US", "FL", "dispatch@example.test", "+15615550101") { IndustryAnswers = IndustryAnswersFixture.AllNo(Now), Attestation = AttestationFixture.Good(Now) });
        Assert.True(created.IsSuccess, created.Error);
        var id = created.Value!.Id;

        await using var conn = new Npgsql.NpgsqlConnection(IntegrationTestConfig.DatabaseUrl!);
        await conn.OpenAsync();
        var rows = await Dapper.SqlMapper.QueryAsync<(string Industry, string Question, bool Answer, string Ip, string Country)>(conn,
            "SELECT industry, question, answer, host(ip_address), visitor_country FROM geek_voice.signup_industry_answers WHERE tenant_id = @id ORDER BY industry", new { id });
        Assert.Equal(IndustryAnswersFixture.Questions.Select(q => q.Code).Order(StringComparer.Ordinal), rows.Select(r => r.Industry));
        Assert.All(rows, r => { Assert.False(r.Answer); Assert.Equal("203.0.113.9", r.Ip); Assert.Equal("US", r.Country); });

        var update = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            Dapper.SqlMapper.ExecuteAsync(conn, "UPDATE geek_voice.signup_industry_answers SET answer = TRUE WHERE tenant_id = @id", new { id }));
        var delete = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            Dapper.SqlMapper.ExecuteAsync(conn, "DELETE FROM geek_voice.signup_industry_answers WHERE tenant_id = @id", new { id }));
        var truncate = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            Dapper.SqlMapper.ExecuteAsync(conn, "TRUNCATE geek_voice.signup_industry_answers"));
        Assert.Contains("kept forever", update.MessageText, StringComparison.Ordinal);
        Assert.Contains("kept forever", delete.MessageText, StringComparison.Ordinal);
        Assert.Contains("kept forever", truncate.MessageText, StringComparison.Ordinal);

        var still = await Dapper.SqlMapper.ExecuteScalarAsync<long>(conn, "SELECT count(*) FROM geek_voice.signup_industry_answers WHERE tenant_id = @id AND answer = FALSE", new { id });
        Assert.Equal(IndustryAnswersFixture.Questions.Length, still);
    }

    [Fact]
    public async Task The_signup_declaration_is_stored_with_the_tenant_and_the_database_refuses_to_change_or_delete_it()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();

        var attestation = AttestationFixture.Good(Now);
        var created = await store.CreateTenantAsync(new(
            "Boca Cool HVAC " + Unique()[..6], "Heating and air conditioning", Now, "US", "FL", "dispatch@example.test", "+15615550101") { IndustryAnswers = IndustryAnswersFixture.AllNo(Now), Attestation = attestation });
        Assert.True(created.IsSuccess, created.Error);
        var id = created.Value!.Id;

        await using var conn = new Npgsql.NpgsqlConnection(IntegrationTestConfig.DatabaseUrl!);
        await conn.OpenAsync();
        var row = await Dapper.SqlMapper.QuerySingleAsync<(string Name, string Title, string Address, string DeclarantAddress, string DeclarantPhone, string DeclarantEmail, string Declaration, string Terms, string Ip, string Country)>(conn,
            "SELECT attestor_name_encrypted, attestor_title_encrypted, business_address_encrypted, declarant_address_encrypted, declarant_phone_encrypted, declarant_email_encrypted, declaration_statement, terms_statement, host(ip_address), visitor_country FROM geek_voice.signup_attestations WHERE tenant_id = @id", new { id });
        Assert.Equal((attestation.AttestorNameEncrypted, attestation.AttestorTitleEncrypted, attestation.BusinessAddressEncrypted, attestation.DeclarantAddressEncrypted, attestation.DeclarantPhoneEncrypted, attestation.DeclarantEmailEncrypted, attestation.DeclarationStatement, attestation.TermsStatement, "203.0.113.9", "US"), row);

        var update = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            Dapper.SqlMapper.ExecuteAsync(conn, "UPDATE geek_voice.signup_attestations SET declaration_statement = 'changed' WHERE tenant_id = @id", new { id }));
        var delete = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            Dapper.SqlMapper.ExecuteAsync(conn, "DELETE FROM geek_voice.signup_attestations WHERE tenant_id = @id", new { id }));
        var truncate = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            Dapper.SqlMapper.ExecuteAsync(conn, "TRUNCATE geek_voice.signup_attestations"));
        Assert.Contains("kept forever", update.MessageText, StringComparison.Ordinal);
        Assert.Contains("kept forever", delete.MessageText, StringComparison.Ordinal);
        Assert.Contains("kept forever", truncate.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_visitors_yes_is_stored_with_no_tenant()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();
        var store = NewRepository();
        var at = Now.AddTicks(Random.Shared.NextInt64(1, 10_000_000) * 10);
        var answers = new VoiceIndustryAnswerSet(
            [
                new VoiceIndustryAnswer("hipaa_healthcare_provider", "Is the business a healthcare provider?", false),
                new VoiceIndustryAnswer("securities_broker_dealer", "Is the business a securities broker-dealer?", true),
            ],
            at, "203.0.113.9", "US");

        var recorded = await store.RecordIndustryRefusalAsync(answers);

        Assert.True(recorded.IsSuccess, recorded.Error);
        Assert.Equal(2, recorded.Value!.Answers);
        await using var conn = new Npgsql.NpgsqlConnection(IntegrationTestConfig.DatabaseUrl!);
        await conn.OpenAsync();
        var rows = await Dapper.SqlMapper.QueryAsync<(string Industry, bool Answer)>(conn,
            "SELECT industry, answer FROM geek_voice.signup_industry_answers WHERE tenant_id IS NULL AND answered_at = @at ORDER BY industry", new { at = at.UtcDateTime });
        Assert.Equal([("hipaa_healthcare_provider", false), ("securities_broker_dealer", true)], rows);
    }

    [Fact]
    public async Task The_old_business_code_field_is_gone_from_the_tenants_table()
    {
        if (!IntegrationTestConfig.IsDatabaseConfigured) return;
        await SqlMigrationTestHelper.ApplyAsync();

        await using var conn = new Npgsql.NpgsqlConnection(IntegrationTestConfig.DatabaseUrl!);
        await conn.OpenAsync();
        var columns = await Dapper.SqlMapper.ExecuteScalarAsync<long>(conn,
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'geek_voice' AND table_name = 'tenants' AND column_name = 'business_activity_code'");
        Assert.Equal(0, columns);
    }
}
